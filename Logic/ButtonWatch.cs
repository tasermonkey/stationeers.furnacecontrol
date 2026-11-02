using System.Collections.Generic;

namespace FurnaceControlMod.Logic
{
    // Spots a button's press from its Setting, read each tick. A button (the game's, and the modular consoles')
    // holds Setting at 1 for 0.55 s, so a tick sees it once or twice: only the rise from 0 counts. A button
    // first seen already at 1 (just linked, or just loaded) doesn't count.
    public class ButtonWatch
    {
        readonly Dictionary<long, bool> _down = new Dictionary<long, bool>();

        public bool Pressed(long button, double setting)
        {
            var down = setting > 0;
            var seen = _down.TryGetValue(button, out var was);
            _down[button] = down;
            return seen && down && !was;
        }
    }
}
