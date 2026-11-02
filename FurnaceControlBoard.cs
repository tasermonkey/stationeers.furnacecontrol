using System;
using System.Collections.Generic;
using System.Linq;
using Assets.Scripts;
using Assets.Scripts.GridSystem;
using Assets.Scripts.Networking;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Electrical;
using Assets.Scripts.Objects.Items;
using Assets.Scripts.Objects.Motherboards;
using Assets.Scripts.Objects.Pipes;
using Cysharp.Threading.Tasks;
using FurnaceControlMod.Logic;
using Objects.Pipes;
using Reagents;
using UnityEngine;

namespace FurnaceControlMod
{
    // The Furnace Control circuitboard. The setup page links devices and gives each a role; the
    // main screen picks a recipe and an amount and follows the batch.
    //
    // A board in another console can be this one's slave (the game's master and slave boards, as the Advanced
    // Airlock's): it shows the master's whole screen, or just its recipe list or its 10-key, and sends its
    // presses to the master. Only the master runs the furnace.
    public class FurnaceControlBoard : Circuitboard
    {
        public const string BoardPrefabName = "CircuitboardFurnaceControl";

        // Board commands (Motherboard.UseComputer). The game's own are 0-20. A client runs its command, then
        // again when the server sends it back, so each must change nothing the second time.
        public const int CommandSetRole = 100;  // reference: the device; referenceInt: its Role (None unlinks)
        public const int CommandStart = 101;    // referenceInt: the recipe's ingot hash; text: the amount
        public const int CommandAbort = 102;
        public const int CommandAcknowledge = 103;
        public const int CommandFlushBox = 104;  // referenceInt: 1 starts Flush Box, 0 cancels it
        public const int CommandRecipePictures = 105; // referenceInt: 1 shows the recipe list as pictures, 0 as names
        public const int CommandPickRecipe = 106;     // referenceInt: the next batch's ingot hash
        public const int CommandSetAmount = 107;      // referenceInt: the next batch's amount
        public const int CommandSlaveScreen = 108;    // reference: a console with this board; referenceInt: its SlaveScreen (None frees it)

        const ushort SyncFlag = 512; // NetworkUpdateFlags: the board's state. Thing to Circuitboard use 1-256 and 2048.

        readonly RoleMap _roles = new RoleMap();
        readonly SlaveMap _slaves = new SlaveMap();
        readonly FurnaceController _controller = new FurnaceController();
        readonly ButtonWatch _buttons = new ButtonWatch(); // the emergency buttons' last readings (server)
        int _recipeHash; // the batch's ingot: the recipe itself is looked up once the game has its recipes
        int _pickedHash; // the next batch's, the same way
        bool _recipePictures;
        bool _reportedError;
        FurnaceScreens _screens;

        public RoleMap Roles => _roles;
        public FurnaceController Controller => _controller;
        public bool RecipePictures => _recipePictures; // a setup option, for everyone at this console
        public RecipeInfo Recipe => _controller.Recipe ?? (_controller.Recipe = FurnaceRecipes.Find(_recipeHash));
        public RecipeInfo PickedRecipe => _controller.PickedRecipe ?? (_controller.PickedRecipe = FurnaceRecipes.Find(_pickedHash));
        public int PickedAmount => _controller.PickedAmount;

        // The board whose furnace this console shows: its master's, if it's a slave.
        public FurnaceControlBoard Main => MasterMotherboard is FurnaceControlBoard master && master != null && master != this ? master : this;
        public bool IsSlave => Main != this;

        // What this console shows: everything, unless it's a slave given one job.
        public SlaveScreen Shows => IsSlave ? Main._slaves[ReferenceId] : SlaveScreen.Full;

        public override void Awake()
        {
            base.Awake();
            _screens = new FurnaceScreens(this);
            RefreshLoop().Forget();
        }

        // ---- Devices and roles ----

        public static DeviceKind KindOf(Device device)
        {
            switch (device)
            {
                case AdvancedFurnace _: return DeviceKind.Furnace;
                case VolumePump _: return DeviceKind.VolumePump;
                case PipeAnalysizer _: return DeviceKind.PipeAnalyzer;
                case Mixer _: return DeviceKind.Mixer;
                case ActiveVent _: return DeviceKind.ActiveVent;
                case GasSensor _: return DeviceKind.GasSensor;
                default: return IsButton(device) ? DeviceKind.Button : DeviceKind.None;
            }
        }

        static bool IsButton(Device device) =>
            (device is LogicButton || device.PrefabName?.Contains("Button") == true) && device.CanLogicRead(LogicType.Setting);

        public bool IsLinked(long device) => LinkedDevices.Any(linked => linked != null && linked.ReferenceId == device);

        // The device with this role, if it is linked and on the console's network.
        public Device DeviceFor(Role role)
        {
            var id = _roles.DeviceFor(role, IsLinked);
            var device = id == 0 ? null : LinkedDevices.FirstOrDefault(linked => linked != null && linked.ReferenceId == id);
            return device != null && IsDeviceConnected(device) ? device : null;
        }

        // Every device with a shared role (the emergency buttons), linked and on the console's network.
        public List<Device> DevicesFor(Role role) =>
            _roles.DevicesFor(role, IsLinked)
                .Select(id => LinkedDevices.FirstOrDefault(linked => linked != null && linked.ReferenceId == id))
                .Where(device => device != null && IsDeviceConnected(device))
                .ToList();

        public List<Role> MissingRoles() => Logic.Roles.Required.Where(role => DeviceFor(role) == null).ToList();

        public override bool IsOperable => IsSlave ? Main.IsOperable : MissingRoles().Count == 0;

        public override bool CanDeviceLink(Device device) => KindOf(device) != DeviceKind.None;

        // On the setup page a click moves a device to its next free role, or links it; past the last, it unlinks.
        void OnDeviceButton(ComputerButton button)
        {
            var device = button.AssignedDevice;
            var kind = KindOf(device);
            if (kind == DeviceKind.None) return;
            var role = _roles.NextFor(device.ReferenceId, kind, IsLinked);
            UseComputer(CommandSetRole, ReferenceId, device.ReferenceId,(int)role, sendToAll: true);
        }

        void SetRole(Device device, Role role)
        {
            if (device == null) return;
            if (role != Role.None && Logic.Roles.KindOf(role) != KindOf(device)) return;
            _roles.Set(device.ReferenceId, role);
            var linked = LinkedDevices.Contains(device);
            if (role == Role.None && linked) RemoveLinkedDevice(device);
            if (role != Role.None && !linked) AddLinkedDevice(device);
            OnDeviceListChanged(device);
            Changed();
        }

        // The setup page's buttons: each device's role after its name, and clicks that cycle roles. A slave's is the
        // game's own: just its master's console.
        public override async UniTask DeviceListChangeTask()
        {
            await base.DeviceListChangeTask();
            if (IsSlave) return;
            foreach (var button in Buttons)
            {
                var device = button?.AssignedDevice;
                if (device == null || button.Button == null) continue;
                if ((device as Computer)?.CurrentMotherboard is FurnaceControlBoard other)
                {
                    LabelConsole(button, other);
                    continue;
                }
                var role = IsLinked(device.ReferenceId) ? _roles[device.ReferenceId] : Role.None;
                button.Label.text = role == Role.None
                    ? device.DisplayName
                    : $"{device.DisplayName} <b>[{Logic.Roles.Label(role)}]</b>"; // the button's own color: it reads on every state
                var captured = button;
                button.Button.onClick.RemoveAllListeners();
                button.Button.onClick.AddListener(() => OnDeviceButton(captured));
                button.SetColor();
            }
        }

        // Another console with this board: a click cycles it through the slave screens, then frees it. One that is
        // another furnace's master or slave can't be clicked, as on the game's own boards.
        void LabelConsole(ComputerButton button, FurnaceControlBoard other)
        {
            var name = button.AssignedDevice.DisplayName;
            var ours = other.MasterMotherboard == this;
            var taken = !ours && (other.Slaves.Count > 0 || other.MasterMotherboard != null);
            if (ours) button.Label.text = $"{name} <b>[{SlaveScreens.Label(_slaves[other.ReferenceId])}]</b>";
            else if (taken) button.Label.text = $"{name} <b>[{(other.Slaves.Count > 0 ? "MASTER" : "SLAVE")}]</b>";
            else button.Label.text = name;
            button.Button.onClick.RemoveAllListeners();
            button.Button.onClick.AddListener(() => OnConsoleButton(other));
            button.SetColor();
            var colors = button.Button.colors;
            if (ours) colors.normalColor = Color.green; // as a linked device
            button.Button.colors = colors;
            button.Button.interactable = !taken;
        }

        void OnConsoleButton(FurnaceControlBoard other)
        {
            if (other == null || other.ParentComputer == null) return;
            var screen = other.MasterMotherboard == this ? _slaves[other.ReferenceId] : SlaveScreen.None;
            UseComputer(CommandSlaveScreen, ReferenceId, other.ParentComputer.AsThing().ReferenceId, (int)SlaveScreens.Next(screen), sendToAll: true);
        }

        // Makes the console's board a slave showing this screen, or frees it (None), through the game's own
        // AddSlave and RemoveSlave.
        void SetSlaveScreen(Computer console, SlaveScreen screen)
        {
            if (!(console?.CurrentMotherboard is FurnaceControlBoard slave) || slave == this) return;
            if (slave.MasterMotherboard != null && slave.MasterMotherboard != this) return; // another furnace's
            if (screen == SlaveScreen.None)
            {
                if (slave.MasterMotherboard == this) base.MotherboardCommand((int)ButtonCommands.RemoveSlave, console, -1, "");
            }
            else
                base.MotherboardCommand((int)ButtonCommands.AddSlave, console, -1, "");
            _slaves.Set(slave.ReferenceId, screen);
            slave._screens?.MarkDirty();
            Changed();
            OnDeviceListChanged();
        }

        // ---- Commands ----
        // A slave's go to its master, which runs the furnace.

        public void SendStart(RecipeInfo recipe, int amount) =>
            Send(CommandStart, recipe.OutputHash, BatchPlan.ClampAmount(amount).ToString());

        public void SendAbort() => Send(CommandAbort, 0);

        public void SendAcknowledge() => Send(CommandAcknowledge, 0);

        public void SendFlushBox(bool start) => Send(CommandFlushBox, start ? 1 : 0);

        public void SendRecipePictures(bool pictures) => Send(CommandRecipePictures, pictures ? 1 : 0);

        public void SendPick(RecipeInfo recipe) => Send(CommandPickRecipe, recipe.OutputHash);

        public void SendAmount(int amount) => Send(CommandSetAmount, amount);

        void Send(int command, int referenceInt, string text = "") =>
            UseComputer(command, Main.ReferenceId, Main.ReferenceId, referenceInt, sendToAll: true, text);

        public override void MotherboardCommand(int command, Thing reference, int referenceInt, string text)
        {
            switch (command)
            {
                case CommandSetRole:
                    SetRole(reference as Device, (Role)referenceInt);
                    return;
                case CommandStart:
                    var recipe = FurnaceRecipes.Find(referenceInt);
                    int.TryParse(text, out var amount);
                    if (IsOperable && _controller.Start(recipe, amount))
                    {
                        _recipeHash = recipe.OutputHash;
                        Changed();
                    }
                    return;
                case CommandAbort:
                    if (_controller.Abort()) Changed();
                    return;
                case CommandAcknowledge:
                    if (_controller.Acknowledge()) Changed();
                    return;
                case CommandFlushBox:
                    if ((referenceInt == 0 || IsOperable) && _controller.FlushBox(referenceInt != 0)) Changed();
                    return;
                case CommandRecipePictures:
                    if (_recipePictures == (referenceInt != 0)) return;
                    _recipePictures = referenceInt != 0;
                    Changed();
                    return;
                case CommandPickRecipe:
                    if (_controller.Pick(FurnaceRecipes.Find(referenceInt)))
                    {
                        _pickedHash = referenceInt;
                        Changed();
                    }
                    return;
                case CommandSetAmount:
                    if (_controller.SetAmount(referenceInt)) Changed();
                    return;
                case CommandSlaveScreen:
                    SetSlaveScreen(reference as Computer, (SlaveScreen)referenceInt);
                    return;
                // The game's own link and unlink, from anywhere but this board's buttons. Loading a save links
                // each saved device this way, after DeserializeSave has put back its role.
                case (int)ButtonCommands.AddDevice:
                    if (!(reference is Device added) || KindOf(added) == DeviceKind.None) return;
                    if (!IsLinked(added.ReferenceId))
                    {
                        var role = _roles.ForLinking(added.ReferenceId, KindOf(added), IsLinked);
                        if (role == Role.None) return;
                        _roles.Set(added.ReferenceId, role);
                    }
                    break;
                case (int)ButtonCommands.RemoveDevice:
                    if (reference is Device removed) _roles.Set(removed.ReferenceId, Role.None);
                    break;
                // The game's own: loading a save makes each slave again with AddSlaveDirect, the slave's screen
                // already back from this board's save
                case (int)ButtonCommands.AddSlave:
                case (int)ButtonCommands.AddSlaveDirect:
                    base.MotherboardCommand(command, reference, referenceInt, text);
                    MarkScreensDirty();
                    OnDeviceListChanged();
                    return;
            }
            base.MotherboardCommand(command, reference, referenceInt, text);
        }

        // The state changed: send it to the clients, and redraw.
        void Changed()
        {
            if (NetworkManager.IsServer) NetworkUpdateFlags |= SyncFlag;
            MarkScreensDirty();
        }

        // This console's screen and its slaves', which show this board's state.
        void MarkScreensDirty()
        {
            _screens?.MarkDirty();
            foreach (var slave in Slaves.OfType<FurnaceControlBoard>())
                slave._screens?.MarkDirty();
        }

        // ---- The furnace, each power tick on the server ----

        public override void OnPowerTick()
        {
            base.OnPowerTick();
            if (!GameManager.RunSimulation || ParentComputer == null || IsSlave) return; // a slave's master runs the furnace
            var computer = ParentComputer.AsThing();
            if (computer == null || !computer.OnOff || !computer.Powered) return;
            try
            {
                WatchEmergencyButtons();
                if (IsOperable || _controller.State == BatchState.Emergency) Run();
                else StopPumps();
            }
            catch (Exception e)
            {
                if (_reportedError) return;
                _reportedError = true;
                Debug.LogError("[FurnaceControl] power tick: " + e);
            }
        }

        void WatchEmergencyButtons()
        {
            var pressed = false;
            foreach (var button in DevicesFor(Role.EmergencyButton))
                pressed |= _buttons.Pressed(button.ReferenceId, Get(button, LogicType.Setting)); // every button, to keep each one's last reading
            if (!pressed || !_controller.Emergency()) return;
            Debug.Log("[FurnaceControl] emergency button pressed");
            Changed();
        }

        void Run()
        {
            if (_controller.Recipe == null && _recipeHash != 0) _controller.Recipe = FurnaceRecipes.Find(_recipeHash);
            var state = _controller.State;
            var made = _controller.Made;
            var flush = _controller.Flush;
            var commands = _controller.Tick(Read());
            Write(commands);
            if (_controller.State != state || _controller.Made != made || _controller.Flush != flush) Changed();
        }

        Readings Read()
        {
            var furnace = DeviceFor(Role.Furnace);
            var readings = new Readings
            {
                FurnaceOn = furnace != null && furnace.OnOff, // none only in an emergency: the rest read -1
                FurnaceOpen = furnace != null && furnace.IsOpen,
                FurnaceTempK = Get(furnace, LogicType.Temperature),
                FurnacePressureKPa = Get(furnace, LogicType.Pressure),
                RecipeHash = (int)Get(furnace, LogicType.RecipeHash),
                Melted = Get(furnace, LogicType.Reagents),
                Holdings = Holdings(furnace),
                FuelLine = LineOf(DeviceFor(Role.FuelLine)),
                CoolantLine = LineOf(DeviceFor(Role.CoolantLine)),
                H2TempK = Get(DeviceFor(Role.H2Line), LogicType.Temperature),
                O2TempK = Get(DeviceFor(Role.O2Line), LogicType.Temperature),
            };
            var sensor = DeviceFor(Role.BoxSensor);
            readings.BoxPressureKPa = Get(sensor, LogicType.Pressure);
            readings.BoxTempK = Get(sensor, LogicType.Temperature);
            return readings;
        }

        // What's melted in, and the ore waiting in the import slot.
        public static List<Holding> Holdings(Device furnace)
        {
            var holdings = new List<Holding>();
            if (furnace == null) return holdings;
            AddHoldings(holdings, furnace.ReagentMixture);
            if (furnace is AdvancedFurnace advanced && advanced.ImportingThing is Item item)
                AddHoldings(holdings, item.GetTotalReagentMixture());
            return holdings;
        }

        static void AddHoldings(List<Holding> holdings, ReagentMixture mixture)
        {
            if (mixture == null) return;
            foreach (var reagent in Reagent.AllReagents)
            {
                var quantity = mixture.Get(reagent);
                if (quantity > 0) holdings.Add(new Holding(reagent.Hash, reagent.DisplayName, quantity));
            }
        }

        static Line LineOf(Device analyzer) => new Line
        {
            TempK = Get(analyzer, LogicType.Temperature),
            PressureKPa = Get(analyzer, LogicType.Pressure),
            TotalMoles = Get(analyzer, LogicType.TotalMoles),
            VolumeLitres = Get(analyzer, LogicType.Volume),
        };

        public static double Get(Device device, LogicType type) =>
            device != null && device.CanLogicRead(type) ? device.GetLogicValue(type) : -1;

        void Write(Commands commands)
        {
            var furnace = DeviceFor(Role.Furnace);
            Set(furnace, LogicType.SettingInput, commands.SettingInput);
            Set(furnace, LogicType.SettingOutput, commands.SettingOutput);
            Set(furnace, LogicType.Open, commands.Open ? 1 : 0);
            if (commands.Ignite && furnace != null) furnace.SetLogicValue(LogicType.Activate, 1); // it resets itself

            var fuelPump = DeviceFor(Role.FuelPump);
            Set(fuelPump, LogicType.Setting, commands.FuelPumpSpeedLitres);
            Set(fuelPump, LogicType.On, commands.FuelPumpOn ? 1 : 0);
            var coolantPump = DeviceFor(Role.CoolantPump);
            Set(coolantPump, LogicType.Setting, commands.CoolantPumpSpeedLitres);
            Set(coolantPump, LogicType.On, commands.CoolantPumpOn ? 1 : 0);

            var mixer = DeviceFor(Role.FuelMixer);
            if (commands.MixerSetting.HasValue) Set(mixer, LogicType.Setting, commands.MixerSetting.Value);
            Set(mixer, LogicType.On, commands.MixerOn ? 1 : 0);

            Vent(DeviceFor(Role.BoxFillVent), commands.FillVent, VentDirection.Outward, 0);
            Vent(DeviceFor(Role.BoxEmptyVent), commands.EmptyVent, VentDirection.Inward,
                FurnaceController.VentInternalPressureKPa);
        }

        static void Vent(Device vent, VentCommand command, VentDirection direction, double internalPressureKPa)
        {
            Set(vent, LogicType.Mode, (int)direction);
            Set(vent, LogicType.PressureExternal, command.ExternalPressureKPa);
            Set(vent, LogicType.PressureInternal, internalPressureKPa);
            Set(vent, LogicType.On, command.On ? 1 : 0);
        }

        // A device the board can't run any more mustn't go on pumping.
        void StopPumps()
        {
            Set(DeviceFor(Role.FuelPump), LogicType.On, 0);
            Set(DeviceFor(Role.CoolantPump), LogicType.On, 0);
            Set(DeviceFor(Role.Furnace), LogicType.SettingInput, 0);
        }

        // Writes only a change, so the game sends nothing for a value that stays put.
        static void Set(Device device, LogicType type, double value)
        {
            if (device == null || !device.CanLogicWrite(type)) return;
            if (device.CanLogicRead(type) && Math.Abs(device.GetLogicValue(type) - value) < 1e-3) return;
            device.SetLogicValue(type, value);
        }

        // ---- The screen, on every player's machine ----

        async UniTaskVoid RefreshLoop()
        {
            var cancel = this.GetCancellationTokenOnDestroy();
            while (!cancel.IsCancellationRequested)
            {
                await UniTask.Delay(250, ignoreTimeScale: true, PlayerLoopTiming.Update, cancel).SuppressCancellationThrow();
                if (cancel.IsCancellationRequested || GameManager.GameState != GameState.Running) continue;
                try { _screens?.Refresh(); }
                catch (Exception e)
                {
                    if (_reportedError) continue;
                    _reportedError = true;
                    Debug.LogError("[FurnaceControl] screen: " + e);
                }
            }
        }

        public override void RefreshScreen()
        {
            base.RefreshScreen();
            _screens?.MarkDirty();
        }

        // ---- Save, and sync to clients ----

        public override ThingSaveData SerializeSave()
        {
            ThingSaveData saveData = new FurnaceControlSaveData();
            InitialiseSaveData(ref saveData);
            return saveData;
        }

        public override void InitialiseSaveData(ref ThingSaveData savedData) // protected in the game; publicized here
        {
            base.InitialiseSaveData(ref savedData);
            if (!(savedData is FurnaceControlSaveData saveData)) return;
            saveData.Roles = _roles.Entries
                .Select(entry => new RoleDevice { ReferenceId = entry.Key, Role = entry.Value })
                .ToArray();
            saveData.State = (int)_controller.State;
            saveData.RecipeHash = _recipeHash;
            saveData.Amount = _controller.Amount;
            saveData.Made = _controller.Made;
            saveData.Flush = (int)_controller.Flush;
            saveData.RecipePictures = _recipePictures;
            saveData.PickedRecipe = _pickedHash;
            saveData.PickedAmount = _controller.PickedAmount;
            saveData.SlaveScreens = _slaves.Entries
                .Select(entry => new SlaveConsole { ReferenceId = entry.Key, Screen = entry.Value })
                .ToArray();
        }

        public override void DeserializeSave(ThingSaveData saveData)
        {
            base.DeserializeSave(saveData);
            if (!(saveData is FurnaceControlSaveData furnaceData)) return;
            _roles.Clear();
            if (furnaceData.Roles != null)
                foreach (var device in furnaceData.Roles)
                    if (device != null) _roles.Set(device.ReferenceId, device.Role);
            _slaves.Clear();
            if (furnaceData.SlaveScreens != null)
                foreach (var slave in furnaceData.SlaveScreens)
                    if (slave != null) _slaves.Set(slave.ReferenceId, slave.Screen);
            _recipePictures = furnaceData.RecipePictures;
            SetPicks(furnaceData.PickedRecipe, furnaceData.PickedAmount);
            SetState((BatchState)furnaceData.State, furnaceData.RecipeHash, furnaceData.Amount, furnaceData.Made, (BoxFlush)furnaceData.Flush);
        }

        void SetState(BatchState state, int recipeHash, int amount, int made, BoxFlush flush)
        {
            _controller.State = state;
            _controller.Flush = flush;
            _recipeHash = recipeHash;
            _controller.Recipe = FurnaceRecipes.Find(recipeHash);
            _controller.Amount = amount;
            _controller.Made = made;
            MarkScreensDirty();
        }

        void SetPicks(int recipeHash, int amount)
        {
            _pickedHash = recipeHash;
            _controller.PickedRecipe = FurnaceRecipes.Find(recipeHash);
            _controller.PickedAmount = amount;
        }

        public override void BuildUpdate(RocketBinaryWriter writer, ushort networkUpdateType)
        {
            base.BuildUpdate(writer, networkUpdateType);
            if (IsNetworkUpdateRequired(SyncFlag, networkUpdateType)) WriteState(writer);
        }

        public override void ProcessUpdate(RocketBinaryReader reader, ushort networkUpdateType)
        {
            base.ProcessUpdate(reader, networkUpdateType);
            if (IsNetworkUpdateRequired(SyncFlag, networkUpdateType)) ReadState(reader);
        }

        public override void SerializeOnJoin(RocketBinaryWriter writer)
        {
            base.SerializeOnJoin(writer);
            WriteState(writer);
        }

        public override void DeserializeOnJoin(RocketBinaryReader reader)
        {
            base.DeserializeOnJoin(reader);
            ReadState(reader);
        }

        void WriteState(RocketBinaryWriter writer)
        {
            writer.WriteInt32((int)_controller.State);
            writer.WriteInt32(_recipeHash);
            writer.WriteInt32(_controller.Amount);
            writer.WriteInt32(_controller.Made);
            writer.WriteInt32((int)_controller.Flush);
            writer.WriteBoolean(_recipePictures);
            writer.WriteInt32(_pickedHash);
            writer.WriteInt32(_controller.PickedAmount);
            var roles = _roles.Entries.ToList();
            writer.WriteInt32(roles.Count);
            foreach (var entry in roles)
            {
                writer.WriteInt64(entry.Key);
                writer.WriteInt32((int)entry.Value);
            }
            var slaves = _slaves.Entries.ToList();
            writer.WriteInt32(slaves.Count);
            foreach (var entry in slaves)
            {
                writer.WriteInt64(entry.Key);
                writer.WriteInt32((int)entry.Value);
            }
        }

        void ReadState(RocketBinaryReader reader)
        {
            var state = (BatchState)reader.ReadInt32();
            var recipeHash = reader.ReadInt32();
            var amount = reader.ReadInt32();
            var made = reader.ReadInt32();
            var flush = (BoxFlush)reader.ReadInt32();
            _recipePictures = reader.ReadBoolean();
            SetPicks(reader.ReadInt32(), reader.ReadInt32());
            _roles.Clear();
            var count = reader.ReadInt32();
            for (var i = 0; i < count; i++)
            {
                var device = reader.ReadInt64();
                _roles.Set(device, (Role)reader.ReadInt32());
            }
            _slaves.Clear();
            count = reader.ReadInt32();
            for (var i = 0; i < count; i++)
            {
                var slave = reader.ReadInt64();
                _slaves.Set(slave, (SlaveScreen)reader.ReadInt32());
            }
            SetState(state, recipeHash, amount, made, flush);
            if (GameManager.GameState == GameState.Running) OnDeviceListChanged(); // relabel the setup page
        }
    }
}
