using FurnaceControlMod.Logic;
using Xunit;

namespace FurnaceControl.Tests
{
    public class ButtonWatchTests
    {
        readonly ButtonWatch _watch = new ButtonWatch();

        [Fact]
        public void APress_CountsOnce_ThoughTwoTicksSeeIt()
        {
            Assert.False(_watch.Pressed(1, 0));
            Assert.True(_watch.Pressed(1, 1));
            Assert.False(_watch.Pressed(1, 1)); // Setting holds 1 for 0.55 s: a second tick can see it
            Assert.False(_watch.Pressed(1, 0));
            Assert.True(_watch.Pressed(1, 1));  // the next press
        }

        [Fact]
        public void AButtonFirstSeenDown_DoesNotCount()
        {
            Assert.False(_watch.Pressed(1, 1)); // just linked, or just loaded, mid-press
            Assert.False(_watch.Pressed(1, 0));
            Assert.True(_watch.Pressed(1, 1));
        }

        [Fact]
        public void EachButton_IsWatchedOnItsOwn()
        {
            _watch.Pressed(1, 0);
            _watch.Pressed(2, 0);

            Assert.True(_watch.Pressed(2, 1));
            Assert.False(_watch.Pressed(1, 0));
        }
    }
}
