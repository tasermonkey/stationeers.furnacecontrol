using System;
using System.Collections.Generic;

namespace FurnaceControlMod.Logic
{
    // Saved and synced by number: add new states at the end.
    public enum BatchState
    {
        Idle,       // no batch: waiting for a recipe and an amount
        Active,     // heating, and taking in ore until the batch is complete
        Unloading,  // done: the furnace is open until its ingots are out
        Aborting,   // aborted: the furnace is open until it's empty
        Done,       // unloaded: shows what was made until it's acknowledged
        Emergency,  // an emergency button: the furnace vented and emptied, the box held empty until it is
    }

    // Flush Box: empty the box, then fill it again. Saved and synced by number.
    public enum BoxFlush { None, Emptying, Refilling }

    public class Line
    {
        public double TempK;
        public double PressureKPa;
        public double TotalMoles;
        public double VolumeLitres;
    }

    public class Readings
    {
        public bool FurnaceOn;
        public bool FurnaceOpen;
        public double FurnaceTempK;
        public double FurnacePressureKPa;
        public int RecipeHash;      // the ingot forming, or 0
        public double Melted;       // the furnace's Reagents: everything melted in
        public List<Holding> Holdings = new List<Holding>(); // melted in, plus the ore waiting in the import slot
        public Line FuelLine = new Line();
        public Line CoolantLine = new Line();
        public double H2TempK;
        public double O2TempK;
        public double BoxPressureKPa;
        public double BoxTempK; // the gas sensor in the box
    }

    public class VentCommand
    {
        public bool On;
        public double ExternalPressureKPa;
    }

    // What the board writes to its devices per tick.
    public class Commands
    {
        public double SettingInput;
        public double SettingOutput;
        public bool Open;
        public bool Ignite;
        public bool FuelPumpOn;
        public bool CoolantPumpOn;
        public double FuelPumpSpeedLitres;
        public double CoolantPumpSpeedLitres;
        public bool MixerOn;
        public double? MixerSetting;
        public VentCommand FillVent = new VentCommand();
        public VentCommand EmptyVent = new VentCommand();
    }

    public class FurnaceController
    {
        public const double IgniteBelowTempK = 600;         // Fuel Pump: light the furnace at or below this
        public const double ColdTempK = 300;                // Fuel Pump: a furnace reading 0 K counts as this
        public const double NoLimitTempK = 10000;           // Fuel Pump: a recipe's top K is counted at most this
        public const double GasR = 8314.46;                 // 1000 x the gas constant: kPa from n, T, V
        public const double CoolantMove = 250000;           // Coolant Pump: about 30 moles a tick at the recipe's top K
        public const double FurnacePressureCapKPa = 55000;  // the furnace's own limit
        public const double InputPumpSpeedLitres = 100;     // SettingInput during a batch: the input pump wide open
        public const double OutputPumpSpeedLitres = 30;     // Release: SettingOutput while over the recipe's top kPa
        public const double FlushAbovePressureKPa = 40000;  // Release: not in a batch, open the output above this
        public const double FuelTopUpPressureKPa = 5000;    // Fuel Mixer: fill the fuel line to this
        public const double BoxTargetPressureKPa = 101;     // the box is held at this
        public const double BoxWarnPressureKPa = 150;       // the screen warns above this: an Iron Window takes
                                                            // 200 kPa across it, and the game stresses it from 80% (160)
        public const double VentInternalPressureKPa = 55000; // the empty vent's pipe can take this (Furnace Box)
        public const double FlushEmptyPressureKPa = 1;      // Flush Box empties the box to this: 1% of the old air
        public const double FlushFullPressureKPa = 100;     // ... and refills it to this, the vents then holding 101
        public const double OutputPumpWideLitres = 100;     // the furnace's output wide open: litres of its gas a tick
        public const double EmergencyEmptyPressureKPa = 1;  // Emergency: the furnace counts as vented at this

        public BatchState State { get; set; }
        public RecipeInfo Recipe { get; set; }
        public int Amount { get; set; }
        public int Made { get; set; }
        public BatchPlan Plan { get; private set; } // this tick's, while Active
        public BoxFlush Flush { get; set; }
        public RecipeInfo PickedRecipe { get; set; } // the next batch's, picked on any of the furnace's consoles
        public int PickedAmount { get; set; }        // ... and its amount, or 0

        public bool IsBusy => State == BatchState.Active || State == BatchState.Unloading || State == BatchState.Aborting
                              || State == BatchState.Emergency;

        public bool Pick(RecipeInfo recipe)
        {
            if (recipe == null || IsBusy || recipe.OutputHash == PickedRecipe?.OutputHash) return false;
            PickedRecipe = recipe;
            return true;
        }

        public bool SetAmount(int amount)
        {
            amount = BatchPlan.ClampAmount(amount);
            if (IsBusy || amount == PickedAmount) return false;
            PickedAmount = amount;
            return true;
        }

        public bool Start(RecipeInfo recipe, int amount)
        {
            if (recipe == null || IsBusy) return false;
            Recipe = recipe;
            Amount = BatchPlan.ClampAmount(amount);
            Made = 0;
            Plan = null;
            State = BatchState.Active;
            return true;
        }

        public bool Abort()
        {
            if (State != BatchState.Active && State != BatchState.Unloading) return false;
            State = BatchState.Aborting;
            Plan = null;
            return true;
        }

        public bool FlushBox(bool start)
        {
            if (State == BatchState.Emergency || start == (Flush != BoxFlush.None)) return false;
            Flush = start ? BoxFlush.Emptying : BoxFlush.None;
            return true;
        }

        public bool Emergency()
        {
            if (State == BatchState.Emergency) return false;
            State = BatchState.Emergency;
            Plan = null;
            Made = 0;
            Flush = BoxFlush.Emptying;
            return true;
        }

        public bool Acknowledge()
        {
            if (State != BatchState.Done) return false;
            State = BatchState.Idle;
            return true;
        }

        public Commands Tick(Readings readings)
        {
            Advance(readings);
            var commands = new Commands
            {
                Open = State == BatchState.Unloading || State == BatchState.Aborting || State == BatchState.Emergency,
                SettingInput = State == BatchState.Active ? InputPumpSpeedLitres : 0,
                SettingOutput = Output(readings),
            };
            if (State == BatchState.Active && readings.FurnaceOn)
            {
                FuelPump(readings, commands);
                CoolantPump(readings, commands);
            }
            if (State != BatchState.Emergency) Mixer(readings, commands); // nothing feeds the furnace in one
            Box(readings.BoxPressureKPa, commands);
            return commands;
        }

        void Advance(Readings readings)
        {
            Plan = State == BatchState.Active && Recipe != null ? BatchPlan.For(Recipe, Amount, readings.Holdings) : null;
            switch (State)
            {
                case BatchState.Active:
                    if (Recipe == null)
                        State = BatchState.Idle;
                    else if (readings.FurnaceOpen || BatchPlan.IsDone(Recipe, Plan.Units, readings.RecipeHash, readings.Melted))
                    {
                        // Done, or opened by hand: what comes out is what's in
                        Made = readings.RecipeHash == Recipe.OutputHash ? BatchPlan.Ingots(Recipe, readings.Melted) : 0;
                        State = BatchState.Unloading;
                    }
                    break;
                case BatchState.Unloading:
                    if (readings.Melted <= 0) State = BatchState.Done;
                    break;
                case BatchState.Aborting:
                    if (readings.Melted <= 0) State = BatchState.Idle;
                    break;
                case BatchState.Emergency:
                    if (readings.Melted <= 0 && readings.FurnacePressureKPa <= EmergencyEmptyPressureKPa)
                        State = BatchState.Idle;
                    break;
            }
        }

        double Output(Readings readings)
        {
            var kPa = Math.Min(readings.FurnacePressureKPa, FurnacePressureCapKPa);
            switch (State)
            {
                case BatchState.Active:
                    return kPa > Recipe.MaxPressureKPa ? OutputPumpSpeedLitres : 0;
                case BatchState.Unloading:
                    return 0;
                case BatchState.Emergency:
                    return OutputPumpWideLitres;
                default:
                    return kPa > FlushAbovePressureKPa ? OutputPumpWideLitres : 0;
            }
        }

        void FuelPump(Readings readings, Commands commands)
        {
            var kelvin = readings.FurnaceTempK;
            commands.Ignite = kelvin <= IgniteBelowTempK;
            var underPressure = Recipe.MinPressureKPa > 0 && readings.FurnacePressureKPa < Recipe.MinPressureKPa;
            commands.FuelPumpOn = kelvin < Recipe.MinTempK || underPressure;
            var line = readings.FuelLine;
            if (line.TotalMoles <= 0 || line.TempK <= 0) return; // an empty line, or the analyzer is off
            var hot = Math.Min(Recipe.MaxTempK, NoLimitTempK) / (kelvin > 0 ? kelvin : ColdTempK);
            commands.FuelPumpSpeedLitres =
                hot * Recipe.MinPressureKPa * 500 * line.VolumeLitres / (line.TempK * line.TotalMoles * GasR);
        }

        void CoolantPump(Readings readings, Commands commands)
        {
            var kelvin = readings.FurnaceTempK;
            commands.CoolantPumpOn = kelvin > Recipe.MaxTempK;
            var line = readings.CoolantLine;
            if (line.TotalMoles <= 0) return;
            var over = Math.Max(kelvin / Recipe.MaxTempK, 1);
            commands.CoolantPumpSpeedLitres = over * CoolantMove * line.VolumeLitres / (line.TotalMoles * GasR);
        }

        static void Mixer(Readings readings, Commands commands)
        {
            // Two H2 to one O2 by moles (allowing for each input's temperature), topping up the fuel line.
            if (readings.H2TempK <= 0 || readings.O2TempK <= 0) return;
            commands.MixerSetting = 100 / (1 + readings.O2TempK / (2 * readings.H2TempK));
            commands.MixerOn = readings.FuelLine.PressureKPa >= 0 && readings.FuelLine.PressureKPa < FuelTopUpPressureKPa;
        }

        void Box(double kPa, Commands commands)
        {
            var emergency = State == BatchState.Emergency;
            if (emergency) Flush = BoxFlush.Emptying;
            else if (Flush != BoxFlush.None && kPa < 0) Flush = BoxFlush.None;
            if (!emergency && Flush == BoxFlush.Emptying && kPa <= FlushEmptyPressureKPa) Flush = BoxFlush.Refilling;
            if (Flush == BoxFlush.Refilling && kPa >= FlushFullPressureKPa) Flush = BoxFlush.None;

            var emptying = Flush == BoxFlush.Emptying;
            commands.FillVent.On = !emptying;
            commands.EmptyVent.On = true;
            commands.FillVent.ExternalPressureKPa = BoxTargetPressureKPa;
            commands.EmptyVent.ExternalPressureKPa = emptying ? 0 : BoxTargetPressureKPa;
        }

        public static bool BoxOverPressure(double kPa) => kPa > BoxWarnPressureKPa;
    }
}
