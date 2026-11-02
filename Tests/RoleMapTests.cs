using System.Collections.Generic;
using FurnaceControlMod.Logic;
using Xunit;

namespace FurnaceControl.Tests
{
    public class RoleMapTests
    {
        readonly RoleMap _roles = new RoleMap();
        readonly HashSet<long> _linked = new HashSet<long>();

        bool IsLinked(long device) => _linked.Contains(device);

        // What a click on the setup page does: works out the role, then sets it (and links or unlinks).
        Role Click(long device, DeviceKind kind)
        {
            var role = _roles.NextFor(device, kind, IsLinked);
            _roles.Set(device, role);
            if (role == Role.None) _linked.Remove(device);
            else _linked.Add(device);
            return role;
        }

        [Fact]
        public void Analyzers_TakeTheLineRolesInOrder()
        {
            Assert.Equal(Role.FuelLine, Click(1, DeviceKind.PipeAnalyzer));
            Assert.Equal(Role.CoolantLine, Click(2, DeviceKind.PipeAnalyzer));
            Assert.Equal(Role.H2Line, Click(3, DeviceKind.PipeAnalyzer));
            Assert.Equal(Role.O2Line, Click(4, DeviceKind.PipeAnalyzer));
            Assert.Equal(Role.None, Click(5, DeviceKind.PipeAnalyzer)); // no role left: stays unlinked
            Assert.DoesNotContain(5L, _linked);
        }

        [Fact]
        public void ClickingALinkedDevice_MovesItToTheNextFreeRole_ThenUnlinksIt()
        {
            Click(1, DeviceKind.PipeAnalyzer);                            // fuel
            Click(2, DeviceKind.PipeAnalyzer);                            // coolant
            Assert.Equal(Role.H2Line, Click(1, DeviceKind.PipeAnalyzer)); // skips coolant, taken
            Assert.Equal(Role.O2Line, Click(1, DeviceKind.PipeAnalyzer));
            Assert.Equal(Role.None, Click(1, DeviceKind.PipeAnalyzer));   // past the last: unlinked
            Assert.Equal(Role.FuelLine, Click(1, DeviceKind.PipeAnalyzer)); // and back in at the first free
        }

        [Fact]
        public void SettingTheSameRoleTwice_ChangesNothing()
        {
            // A client runs its own command, then again when the server sends it back
            var role = _roles.NextFor(1, DeviceKind.VolumePump, IsLinked);
            _roles.Set(1, role);
            _linked.Add(1);
            _roles.Set(1, role);

            Assert.Equal(Role.FuelPump, _roles[1]);
        }

        [Fact]
        public void Loading_KeepsEachDevicesSavedRole()
        {
            // The save's roles come back first; then the game links each device again, in its own order
            _roles.Set(1, Role.FuelLine);
            _roles.Set(2, Role.CoolantLine);
            _roles.Set(3, Role.H2Line);
            _roles.Set(4, Role.O2Line);

            foreach (var device in new long[] { 4, 3, 2, 1 })
            {
                _roles.Set(device, _roles.ForLinking(device, DeviceKind.PipeAnalyzer, IsLinked));
                _linked.Add(device);
            }

            Assert.Equal(Role.FuelLine, _roles[1]);
            Assert.Equal(Role.CoolantLine, _roles[2]);
            Assert.Equal(Role.H2Line, _roles[3]);
            Assert.Equal(Role.O2Line, _roles[4]);
        }

        [Fact]
        public void Linking_ADeviceWithoutARole_GivesItTheFirstFree()
        {
            Click(1, DeviceKind.PipeAnalyzer); // fuel

            Assert.Equal(Role.CoolantLine, _roles.ForLinking(2, DeviceKind.PipeAnalyzer, IsLinked));
        }

        [Fact]
        public void Linking_ADeviceWhoseRoleWasTaken_GivesItTheFirstFree()
        {
            _roles.Set(1, Role.FuelLine); // saved, but not linked yet
            Click(2, DeviceKind.PipeAnalyzer); // takes fuel, as 1 isn't linked

            Assert.Equal(Role.CoolantLine, _roles.ForLinking(1, DeviceKind.PipeAnalyzer, IsLinked));
        }

        [Fact]
        public void AnUnlinkedDevicesRole_IsFree()
        {
            Click(1, DeviceKind.Furnace);
            _linked.Remove(1); // destroyed, or unlinked by the game

            Assert.Equal(Role.Furnace, Click(2, DeviceKind.Furnace));
            Assert.Equal(2, _roles.DeviceFor(Role.Furnace, IsLinked));
        }

        [Fact]
        public void Set_TakesTheRoleFromItsLastHolder()
        {
            _roles.Set(1, Role.BoxFillVent);
            _roles.Set(2, Role.BoxFillVent);

            Assert.Equal(Role.None, _roles[1]);
            Assert.Equal(Role.BoxFillVent, _roles[2]);
        }

        [Fact]
        public void Missing_ListsTheRolesWithNoLinkedDevice()
        {
            Click(1, DeviceKind.Furnace);
            Click(2, DeviceKind.ActiveVent);

            var missing = _roles.Missing(IsLinked);

            Assert.DoesNotContain(Role.Furnace, missing);
            Assert.DoesNotContain(Role.BoxFillVent, missing);
            Assert.Contains(Role.BoxEmptyVent, missing);
            Assert.Equal(Roles.Required.Length - 2, missing.Count);
        }

        [Fact]
        public void EmergencyButtons_AreExtra_AndAsManyAsYouLike()
        {
            Assert.Equal(Role.EmergencyButton, Click(1, DeviceKind.Button));
            Assert.Equal(Role.EmergencyButton, Click(2, DeviceKind.Button)); // the first keeps it

            Assert.Equal(new long[] { 1, 2 }, _roles.DevicesFor(Role.EmergencyButton, IsLinked));
            Assert.DoesNotContain(Role.EmergencyButton, _roles.Missing(IsLinked));

            Assert.Equal(Role.None, Click(1, DeviceKind.Button)); // a second click unlinks it
            Assert.Equal(new long[] { 2 }, _roles.DevicesFor(Role.EmergencyButton, IsLinked));
        }

        [Fact]
        public void NoEmergencyButton_IsNotMissing()
        {
            Assert.DoesNotContain(Role.EmergencyButton, _roles.Missing(IsLinked));
        }
    }
}
