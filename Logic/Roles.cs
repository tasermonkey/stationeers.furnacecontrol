using System;
using System.Collections.Generic;
using System.Linq;

namespace FurnaceControlMod.Logic
{
    // The kinds of device the board can link; anything else is refused.
    public enum DeviceKind { None, Furnace, VolumePump, PipeAnalyzer, Mixer, ActiveVent, GasSensor, Button }

    // What a linked device does for the board. Saved by name: renaming one stops older saves loading.
    public enum Role
    {
        None,
        Furnace,
        FuelPump,
        CoolantPump,
        FuelLine,
        CoolantLine,
        H2Line,
        O2Line,
        FuelMixer,
        BoxFillVent,
        BoxEmptyVent,
        BoxSensor,
        EmergencyButton,
    }

    public static class Roles
    {
        // Every role, in the order a device of each kind is given them.
        public static readonly Role[] All =
        {
            Role.Furnace,
            Role.FuelPump, Role.CoolantPump,
            Role.FuelLine, Role.CoolantLine, Role.H2Line, Role.O2Line,
            Role.FuelMixer,
            Role.BoxFillVent, Role.BoxEmptyVent,
            Role.BoxSensor,
            Role.EmergencyButton,
        };

        // The roles the board can't run without. An emergency button is extra.
        public static readonly Role[] Required = All.Where(role => role != Role.EmergencyButton).ToArray();

        // A role any number of devices can have at once: every linked button is an emergency button.
        public static bool IsShared(Role role) => role == Role.EmergencyButton;

        public static DeviceKind KindOf(Role role)
        {
            switch (role)
            {
                case Role.Furnace: return DeviceKind.Furnace;
                case Role.FuelPump:
                case Role.CoolantPump: return DeviceKind.VolumePump;
                case Role.FuelLine:
                case Role.CoolantLine:
                case Role.H2Line:
                case Role.O2Line: return DeviceKind.PipeAnalyzer;
                case Role.FuelMixer: return DeviceKind.Mixer;
                case Role.BoxFillVent:
                case Role.BoxEmptyVent: return DeviceKind.ActiveVent;
                case Role.BoxSensor: return DeviceKind.GasSensor;
                case Role.EmergencyButton: return DeviceKind.Button;
                default: return DeviceKind.None;
            }
        }

        public static IEnumerable<Role> For(DeviceKind kind) => All.Where(role => KindOf(role) == kind);

        // The tag on the device's button on the setup page.
        public static string Label(Role role)
        {
            switch (role)
            {
                case Role.Furnace: return "FURNACE";
                case Role.FuelPump: return "FUEL PUMP";
                case Role.CoolantPump: return "COOLANT PUMP";
                case Role.FuelLine: return "FUEL LINE";
                case Role.CoolantLine: return "COOLANT LINE";
                case Role.H2Line: return "H2 IN";
                case Role.O2Line: return "O2 IN";
                case Role.FuelMixer: return "FUEL MIXER";
                case Role.BoxFillVent: return "BOX FILL";
                case Role.BoxEmptyVent: return "BOX EMPTY";
                case Role.BoxSensor: return "BOX SENSOR";
                case Role.EmergencyButton: return "EMERGENCY";
                default: return "";
            }
        }
    }

    // Which device (by its ReferenceId) has which role. A role whose device is no longer linked counts as free,
    // so a destroyed device's role can be taken by the next one linked. A shared role is always free.
    public class RoleMap
    {
        readonly Dictionary<long, Role> _roles = new Dictionary<long, Role>();

        public Role this[long device] => _roles.TryGetValue(device, out var role) ? role : Role.None;

        public IEnumerable<KeyValuePair<long, Role>> Entries => _roles;

        // The linked device with this role, or 0.
        public long DeviceFor(Role role, Func<long, bool> isLinked)
        {
            foreach (var entry in _roles)
                if (entry.Value == role && isLinked(entry.Key)) return entry.Key;
            return 0;
        }

        // Every linked device with this role: for a shared one, as many as there are.
        public List<long> DevicesFor(Role role, Func<long, bool> isLinked) =>
            _roles.Where(entry => entry.Value == role && isLinked(entry.Key)).Select(entry => entry.Key).ToList();

        public bool IsFree(Role role, Func<long, bool> isLinked) => Roles.IsShared(role) || DeviceFor(role, isLinked) == 0;

        // The first free role for a device of this kind, or None.
        public Role FirstFree(DeviceKind kind, Func<long, bool> isLinked) =>
            Roles.For(kind).FirstOrDefault(role => IsFree(role, isLinked));

        // The role a device takes when it is linked: the one it had (a save's), unless another linked device has
        // it since, else the first free one.
        public Role ForLinking(long device, DeviceKind kind, Func<long, bool> isLinked)
        {
            var had = this[device];
            if (had != Role.None && Roles.KindOf(had) == kind)
            {
                var holder = DeviceFor(had, isLinked);
                if (holder == 0 || holder == device) return had;
            }
            return FirstFree(kind, isLinked);
        }

        // The role a click on the device moves it to: the next free one after its own, or None to unlink it.
        // An unlinked device gets the first free role.
        public Role NextFor(long device, DeviceKind kind, Func<long, bool> isLinked)
        {
            var current = this[device];
            if (current == Role.None || !isLinked(device)) return FirstFree(kind, isLinked);
            var roles = Roles.For(kind).ToList();
            for (var i = roles.IndexOf(current) + 1; i < roles.Count; i++)
                if (IsFree(roles[i], isLinked)) return roles[i];
            return Role.None;
        }

        // Gives the device the role (None removes it). The role's previous holder, if any, loses it, unless the
        // role is shared. Setting what is already set changes nothing, so a command can safely run twice.
        public void Set(long device, Role role)
        {
            if (role == Role.None)
            {
                _roles.Remove(device);
                return;
            }
            if (!Roles.IsShared(role))
                foreach (var other in _roles.Where(entry => entry.Value == role && entry.Key != device).Select(entry => entry.Key).ToList())
                    _roles.Remove(other);
            _roles[device] = role;
        }

        // The roles the board needs that have no linked device.
        public List<Role> Missing(Func<long, bool> isLinked) => Roles.Required.Where(role => IsFree(role, isLinked)).ToList();

        public void Clear() => _roles.Clear();
    }
}
