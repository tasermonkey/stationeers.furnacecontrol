using System.Collections.Generic;

namespace FurnaceControlMod.Logic
{
    // What a slave console shows: the master's whole screen, or one of its jobs. Saved by name: renaming one stops
    // older saves loading.
    public enum SlaveScreen { None, Full, Recipes, Amount }

    public static class SlaveScreens
    {
        // A click on the console on the master's setup page: not a slave, then each screen, then not a slave again.
        public static SlaveScreen Next(SlaveScreen screen)
        {
            switch (screen)
            {
                case SlaveScreen.None: return SlaveScreen.Full;
                case SlaveScreen.Full: return SlaveScreen.Recipes;
                case SlaveScreen.Recipes: return SlaveScreen.Amount;
                default: return SlaveScreen.None;
            }
        }

        // The tag on the console's button on the master's setup page.
        public static string Label(SlaveScreen screen)
        {
            switch (screen)
            {
                case SlaveScreen.Full: return "SLAVE: FULL";
                case SlaveScreen.Recipes: return "SLAVE: RECIPES";
                case SlaveScreen.Amount: return "SLAVE: AMOUNT";
                default: return "";
            }
        }
    }

    // Each slave board's screen, by the board's ReferenceId, kept by the master. A slave the map doesn't know
    // (made a slave by the game's own command) shows the full screen.
    public class SlaveMap
    {
        readonly Dictionary<long, SlaveScreen> _screens = new Dictionary<long, SlaveScreen>();

        public SlaveScreen this[long slave] => _screens.TryGetValue(slave, out var screen) ? screen : SlaveScreen.Full;

        public IEnumerable<KeyValuePair<long, SlaveScreen>> Entries => _screens;

        // None forgets the slave. Setting what is already set changes nothing, so a command can safely run twice.
        public void Set(long slave, SlaveScreen screen)
        {
            if (screen == SlaveScreen.None) _screens.Remove(slave);
            else _screens[slave] = screen;
        }

        public void Clear() => _screens.Clear();
    }
}
