using FurnaceControlMod.Logic;
using Xunit;
using static FurnaceControl.Tests.Recipes;

namespace FurnaceControl.Tests
{
    public class FurnaceControllerTests
    {
        readonly FurnaceController _controller = new FurnaceController();

        static Readings Furnace(double kelvin = 450, double kPa = 2000) => new Readings
        {
            FurnaceOn = true,
            FurnaceTempK = kelvin,
            FurnacePressureKPa = kPa,
            FuelLine = new Line { TempK = 300, PressureKPa = 5000, TotalMoles = 10, VolumeLitres = 100 },
            CoolantLine = new Line { TempK = 250, PressureKPa = 5000, TotalMoles = 10, VolumeLitres = 100 },
            H2TempK = 300,
            O2TempK = 300,
            BoxPressureKPa = 101,
            BoxTempK = 295,
        };

        [Fact]
        public void Start_OnlyBetweenBatches()
        {
            Assert.True(_controller.Start(Solder, 100));
            Assert.Equal(BatchState.Active, _controller.State);
            Assert.False(_controller.Start(IronIngot, 5)); // a second start, or the same one sent back, changes nothing
            Assert.Same(Solder, _controller.Recipe);
        }

        [Fact]
        public void Active_TakesInputAndKeepsTheFurnaceShut()
        {
            _controller.Start(Solder, 100);
            var commands = _controller.Tick(Furnace());

            Assert.Equal(FurnaceController.InputPumpSpeedLitres, commands.SettingInput);
            Assert.False(commands.Open);
            Assert.NotNull(_controller.Plan);
        }

        [Fact]
        public void Done_OpensTheFurnaceUntilItIsEmpty_ThenShowsWhatWasMade()
        {
            _controller.Start(Solder, 100);
            var full = Furnace();
            full.RecipeHash = Solder.OutputHash;
            full.Melted = 100;
            full.Holdings.Add(Have(1, 50));
            full.Holdings.Add(Have(2, 50));

            var commands = _controller.Tick(full);
            Assert.Equal(BatchState.Unloading, _controller.State);
            Assert.True(commands.Open);
            Assert.Equal(0, commands.SettingInput);
            Assert.Equal(100, _controller.Made);

            full.Melted = 50; // a stack at a time
            Assert.True(_controller.Tick(full).Open);
            full.Melted = 0;
            Assert.False(_controller.Tick(full).Open);
            Assert.Equal(BatchState.Done, _controller.State);

            Assert.True(_controller.Acknowledge());
            Assert.Equal(BatchState.Idle, _controller.State);
        }

        [Fact]
        public void NotDone_UntilTheIngotForms()
        {
            _controller.Start(Solder, 100);
            var readings = Furnace(kelvin: 600); // all in, but over Solder's 550 K: no RecipeHash
            readings.Melted = 100;
            readings.Holdings.Add(Have(1, 50));
            readings.Holdings.Add(Have(2, 50));

            _controller.Tick(readings);

            Assert.Equal(BatchState.Active, _controller.State);
        }

        [Fact]
        public void Abort_EmptiesTheFurnace_ThenIdles()
        {
            _controller.Start(Solder, 100);
            _controller.Tick(Furnace());
            Assert.True(_controller.Abort());
            Assert.False(_controller.Abort()); // sent back by the server: no change

            var readings = Furnace();
            readings.Melted = 30;
            var commands = _controller.Tick(readings);
            Assert.True(commands.Open);
            Assert.False(commands.FuelPumpOn);

            readings.Melted = 0;
            Assert.False(_controller.Tick(readings).Open);
            Assert.Equal(BatchState.Idle, _controller.State);
        }

        [Fact]
        public void OpenedByHand_Unloads()
        {
            _controller.Start(Solder, 100);
            var readings = Furnace();
            readings.FurnaceOpen = true;
            readings.Melted = 40;

            _controller.Tick(readings);

            Assert.Equal(BatchState.Unloading, _controller.State);
            Assert.Equal(0, _controller.Made); // no ingot forming: it comes out as slag
        }

        [Fact]
        public void FuelPump_RunsUnderTheRecipesLowestTemperature_AndLightsTheFurnace()
        {
            _controller.Start(Solder, 100);

            var cold = _controller.Tick(Furnace(kelvin: 300));
            Assert.True(cold.FuelPumpOn);
            Assert.True(cold.Ignite);
            Assert.True(cold.FuelPumpSpeedLitres > 0);

            var inRange = _controller.Tick(Furnace(kelvin: 450));
            Assert.False(inRange.FuelPumpOn);
            Assert.True(inRange.Ignite); // still at or below 600 K, as the Fuel Pump chip has it
        }

        [Fact]
        public void FuelPump_RunsUnderTheRecipesLowestPressure()
        {
            _controller.Start(Solder, 100);

            Assert.True(_controller.Tick(Furnace(kelvin: 450, kPa: 500)).FuelPumpOn);
        }

        [Fact]
        public void FuelPump_GivesNothingFromAnEmptyLine()
        {
            _controller.Start(Solder, 100);
            var readings = Furnace(kelvin: 300);
            readings.FuelLine.TotalMoles = 0;

            Assert.Equal(0, _controller.Tick(readings).FuelPumpSpeedLitres);
        }

        [Fact]
        public void CoolantPump_RunsOverTheRecipesTopTemperature()
        {
            _controller.Start(Solder, 100);

            var hot = _controller.Tick(Furnace(kelvin: 700));
            Assert.True(hot.CoolantPumpOn);
            Assert.False(hot.FuelPumpOn);
            Assert.False(_controller.Tick(Furnace(kelvin: 500)).CoolantPumpOn);
        }

        [Fact]
        public void Pumps_StayOffBetweenBatches()
        {
            var commands = _controller.Tick(Furnace(kelvin: 300));

            Assert.False(commands.FuelPumpOn);
            Assert.False(commands.CoolantPumpOn);
            Assert.False(commands.Ignite);
        }

        [Fact]
        public void Output_VentsOverTheRecipesTopPressure()
        {
            _controller.Start(Stellite, 25); // 10000-20000 kPa

            Assert.Equal(FurnaceController.OutputPumpSpeedLitres,
                _controller.Tick(Furnace(kelvin: 2000, kPa: 21000)).SettingOutput);
            Assert.Equal(0, _controller.Tick(Furnace(kelvin: 2000, kPa: 15000)).SettingOutput);
        }

        [Fact]
        public void Output_OpensWideBetweenBatchesAboveTheFlushPressure()
        {
            Assert.Equal(100, _controller.Tick(Furnace(kPa: 45000)).SettingOutput);
            Assert.Equal(0, _controller.Tick(Furnace(kPa: 30000)).SettingOutput);
        }

        [Fact]
        public void Mixer_TwoH2ToOneO2_TopsUpTheFuelLine()
        {
            var low = Furnace();
            low.FuelLine.PressureKPa = 1000;

            var commands = _controller.Tick(low);
            Assert.True(commands.MixerOn);
            Assert.Equal(100 * 2.0 / 3, commands.MixerSetting.Value, 6); // same temperatures: 2/3 from input 1 (H2)
        }

        [Fact]
        public void Mixer_TopsUpDuringABatchToo()
        {
            // A hot recipe can empty the line mid-batch; the IC10 chips only topped it up between batches
            _controller.Start(Stellite, 25);
            var empty = Furnace(kelvin: 1045, kPa: 12380);
            empty.FuelLine.PressureKPa = 0;
            empty.FuelLine.TotalMoles = 0;

            Assert.True(_controller.Tick(empty).MixerOn);
        }

        [Fact]
        public void Mixer_StaysOffWhenAnInputReadsNothing()
        {
            var low = Furnace();
            low.FuelLine.PressureKPa = 1000;
            low.H2TempK = -1; // its analyzer is off

            Assert.False(_controller.Tick(low).MixerOn);
        }

        [Fact]
        public void Mixer_StopsWhenTheFuelLineIsFull()
        {
            Assert.False(_controller.Tick(Furnace()).MixerOn); // 5000 kPa
        }

        // As the IC10 chips had it: both vents on, both aiming for 101 kPa. Each stops by itself at its target, so the
        // fill vent works below it and the empty vent above it, the moment heat pushes the box past it.
        [Theory]
        [InlineData(50, 295)]
        [InlineData(101, 295)]
        [InlineData(140, 450)] // hot: still only pressure matters
        [InlineData(-1, 295)]  // the sensor is off: the vents don't need it
        public void Box_BothVentsOn_HoldingItsPressure(double kPa, double kelvin)
        {
            var box = Furnace();
            box.BoxPressureKPa = kPa;
            box.BoxTempK = kelvin;

            var commands = _controller.Tick(box);
            Assert.True(commands.FillVent.On);
            Assert.True(commands.EmptyVent.On);
            Assert.Equal(FurnaceController.BoxTargetPressureKPa, commands.FillVent.ExternalPressureKPa);
            Assert.Equal(FurnaceController.BoxTargetPressureKPa, commands.EmptyVent.ExternalPressureKPa);
        }

        [Fact]
        public void FlushBox_EmptiesItToNearVacuum_ThenRefills()
        {
            var box = Furnace();
            Assert.True(_controller.FlushBox(true));
            Assert.Equal(BoxFlush.Emptying, _controller.Flush);

            var emptying = _controller.Tick(box); // 101 kPa: still emptying
            Assert.False(emptying.FillVent.On);
            Assert.True(emptying.EmptyVent.On);
            Assert.Equal(0, emptying.EmptyVent.ExternalPressureKPa);

            box.BoxPressureKPa = 0.8; // under 1 kPa: refill
            var refilling = _controller.Tick(box);
            Assert.Equal(BoxFlush.Refilling, _controller.Flush);
            Assert.True(refilling.FillVent.On);
            Assert.Equal(FurnaceController.BoxTargetPressureKPa, refilling.FillVent.ExternalPressureKPa);
            Assert.Equal(FurnaceController.BoxTargetPressureKPa, refilling.EmptyVent.ExternalPressureKPa);

            box.BoxPressureKPa = 60;
            _controller.Tick(box);
            Assert.Equal(BoxFlush.Refilling, _controller.Flush);

            box.BoxPressureKPa = 100.5; // back near 101: done
            _controller.Tick(box);
            Assert.Equal(BoxFlush.None, _controller.Flush);
        }

        [Fact]
        public void FlushBox_StartingTwice_ChangesNothing_AndCancelHolds()
        {
            Assert.True(_controller.FlushBox(true));
            Assert.False(_controller.FlushBox(true)); // sent back by the server

            Assert.True(_controller.FlushBox(false));
            Assert.Equal(BoxFlush.None, _controller.Flush);
            var commands = _controller.Tick(Furnace());
            Assert.True(commands.FillVent.On);
            Assert.Equal(FurnaceController.BoxTargetPressureKPa, commands.EmptyVent.ExternalPressureKPa);
        }

        [Fact]
        public void FlushBox_StopsIfTheSensorReadsNothing()
        {
            _controller.FlushBox(true);
            var box = Furnace();
            box.BoxPressureKPa = -1; // the sensor is off: no telling when it's empty

            var commands = _controller.Tick(box);

            Assert.Equal(BoxFlush.None, _controller.Flush);
            Assert.True(commands.FillVent.On);
        }

        [Fact]
        public void FlushBox_RunsDuringABatch_WithoutTouchingIt()
        {
            _controller.Start(Solder, 100);
            _controller.FlushBox(true);

            var commands = _controller.Tick(Furnace());

            Assert.Equal(BatchState.Active, _controller.State);
            Assert.Equal(FurnaceController.InputPumpSpeedLitres, commands.SettingInput);
            Assert.False(commands.FillVent.On);
        }

        [Theory]
        [InlineData(151, true)]   // past the warning, before the game stresses an Iron Window (160 of its 200)
        [InlineData(101, false)]
        [InlineData(-1, false)]   // the sensor is off
        public void Box_OverPressure_Above150KPa(double kPa, bool over) =>
            Assert.Equal(over, FurnaceController.BoxOverPressure(kPa));

        [Fact]
        public void Picks_ChangeOnce()
        {
            // A client runs its own command, then again when the server sends it back
            Assert.True(_controller.Pick(Solder));
            Assert.False(_controller.Pick(Solder));
            Assert.True(_controller.SetAmount(100));
            Assert.False(_controller.SetAmount(100));
            Assert.Same(Solder, _controller.PickedRecipe);
            Assert.Equal(100, _controller.PickedAmount);
        }

        [Fact]
        public void Picks_KeptWithin1To500()
        {
            _controller.SetAmount(600);
            Assert.Equal(500, _controller.PickedAmount);
        }

        [Fact]
        public void Picks_LockedWhileABatchCooks()
        {
            _controller.Pick(Solder);
            _controller.SetAmount(100);
            _controller.Start(Solder, 100);

            Assert.False(_controller.Pick(IronIngot));
            Assert.False(_controller.SetAmount(5));
            Assert.Same(Solder, _controller.PickedRecipe);
            Assert.Equal(100, _controller.PickedAmount);

            _controller.Abort();
            Assert.False(_controller.Pick(IronIngot)); // still emptying
        }

        [Fact]
        public void Picks_OpenAgainOnceTheBatchIsMade()
        {
            _controller.State = BatchState.Done;

            Assert.True(_controller.Pick(IronIngot));
            Assert.True(_controller.SetAmount(5));
        }

        // A hot furnace mid-batch: 2 MPa, 40 melted in, and the box at 101 kPa
        static Readings Cooking()
        {
            var readings = Furnace(kelvin: 500, kPa: 2000);
            readings.Melted = 40;
            readings.Holdings.Add(Have(1, 20));
            readings.Holdings.Add(Have(2, 20));
            return readings;
        }

        [Fact]
        public void Emergency_VentsAndEmptiesTheFurnace_AndStopsFeedingIt()
        {
            _controller.Start(Solder, 100);
            Assert.True(_controller.Emergency());

            var readings = Cooking();
            readings.FurnaceTempK = 200; // cold enough that a batch would light it
            readings.FuelLine.PressureKPa = 100; // low enough that the mixer would top it up
            var commands = _controller.Tick(readings);

            Assert.Equal(BatchState.Emergency, _controller.State);
            Assert.True(commands.Open);
            Assert.Equal(FurnaceController.OutputPumpWideLitres, commands.SettingOutput);
            Assert.Equal(0, commands.SettingInput);
            Assert.False(commands.FuelPumpOn);
            Assert.False(commands.CoolantPumpOn);
            Assert.False(commands.Ignite);
            Assert.False(commands.MixerOn);
        }

        [Fact]
        public void Emergency_HoldsTheBoxEmpty_UntilTheFurnaceIsEmpty()
        {
            _controller.Emergency();
            var readings = Cooking();

            var commands = _controller.Tick(readings);
            Assert.Equal(BoxFlush.Emptying, _controller.Flush);
            Assert.False(commands.FillVent.On);
            Assert.True(commands.EmptyVent.On);
            Assert.Equal(0, commands.EmptyVent.ExternalPressureKPa);

            readings.BoxPressureKPa = 0; // the box is empty, the furnace not yet
            commands = _controller.Tick(readings);
            Assert.Equal(BoxFlush.Emptying, _controller.Flush);
            Assert.False(commands.FillVent.On);
        }

        [Fact]
        public void Emergency_EndsOnceTheFurnaceIsVentedAndEmpty_ThenTheBoxRefills()
        {
            _controller.Emergency();
            var readings = Cooking();
            readings.BoxPressureKPa = 0;

            readings.Melted = 0; // contents out, gas still in
            _controller.Tick(readings);
            Assert.Equal(BatchState.Emergency, _controller.State);

            readings.Holdings.Clear();
            readings.FurnacePressureKPa = 0.5;
            var commands = _controller.Tick(readings);
            Assert.Equal(BatchState.Idle, _controller.State);
            Assert.False(commands.Open);
            Assert.Equal(BoxFlush.Refilling, _controller.Flush);
            Assert.True(commands.FillVent.On);

            readings.BoxPressureKPa = 100;
            _controller.Tick(readings);
            Assert.Equal(BoxFlush.None, _controller.Flush);
        }

        [Fact]
        public void Emergency_BetweenBatches_DoesTheSame()
        {
            var commands = _controller.Tick(Furnace());
            Assert.Equal(0, commands.SettingOutput);

            _controller.Emergency();
            commands = _controller.Tick(Cooking());
            Assert.Equal(FurnaceController.OutputPumpWideLitres, commands.SettingOutput);
            Assert.True(commands.Open);
        }

        [Fact]
        public void Emergency_PressedAgain_ChangesNothing_AndLocksEverythingElse()
        {
            _controller.Pick(Solder);
            Assert.True(_controller.Emergency());
            Assert.False(_controller.Emergency());

            Assert.False(_controller.Start(Solder, 100));
            Assert.False(_controller.Abort());
            Assert.False(_controller.FlushBox(false)); // the box stays held
            Assert.False(_controller.Pick(IronIngot));
            Assert.True(_controller.IsBusy);
        }

        [Fact]
        public void Emergency_KeepsEmptyingTheBox_WithNoSensor()
        {
            _controller.Emergency();
            var readings = Cooking();
            readings.BoxPressureKPa = -1; // the sensor broken, or off

            var commands = _controller.Tick(readings);
            Assert.Equal(BoxFlush.Emptying, _controller.Flush);
            Assert.False(commands.FillVent.On);
        }
    }
}
