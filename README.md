# Furnace Control


A Stationeers mod to control an Advance Furnace.  
A console circuitboard that runs an Advanced Furnace instead of needing to use IC10.  I built this cause I felt like I have made enough versions
of the Furnance Control IC10 scripts.

# Requirements
It needs [BepInEx](https://github.com/BepInEx/BepInEx) with
[StationeersLaunchPad](https://github.com/StationeersLaunchPad/StationeersLaunchPad). In multiplayer, every
player needs it.

## Setting it up

1. Print **Circuitboard (Furnace Control)** in the Electronics Printer, and put it in a Console on the furnace's data network.
2. Use a **screwdriver** on the console: it lists every device on the network. Click a device to link it.
   Click it again to move it to its next role, and once more past the last to unlink it. The role shows after its name.
3. Link all of these. The console shows an error until every required role is filled,  which are extra.

| Device | Roles |
| --- | --- |
| Advanced Furnace | Furnace |
| Volume Pump | Fuel pump, Coolant pump (both feed the furnace's input) |
| Pipe Analyzer | Fuel line, Coolant line, H2 in, O2 in |
| Gas Mixer | Fuel mixer (H2 into its input 1) |
| Active Vent | Box fill, Box empty |
| Gas Sensor | Box sensor |
| Button (the game's, or a modular console's) | Emergency: optional, as many as you like |

Under the device list, **Recipe list** switches the recipe list between the recipes' names and pictures of their ingots, for everyone at that console.

## Building

`dotnet test Tests` runs the tests.

`dotnet build -c Release` builds the mod and copies it to the game's local mods folder
(`Documents\My Games\Stationeers\mods\FurnaceControl\`).

## Attribution

Some usage of AI was used during the producton of this mod.

- It helped me figure out how to get a circuit board created
- Then figure out some of the mixer math
- Help me fix some bugs while trying and erroring
- Help to fix some build problems.
- How to get the Recipes in the game... including those added by mods
- I had it write the tests