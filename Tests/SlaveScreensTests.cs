using FurnaceControlMod.Logic;
using Xunit;

namespace FurnaceControl.Tests
{
    public class SlaveScreensTests
    {
        readonly SlaveMap _slaves = new SlaveMap();

        [Fact]
        public void Clicks_CycleFullRecipesAmount_ThenNotASlave()
        {
            Assert.Equal(SlaveScreen.Full, SlaveScreens.Next(SlaveScreen.None));
            Assert.Equal(SlaveScreen.Recipes, SlaveScreens.Next(SlaveScreen.Full));
            Assert.Equal(SlaveScreen.Amount, SlaveScreens.Next(SlaveScreen.Recipes));
            Assert.Equal(SlaveScreen.None, SlaveScreens.Next(SlaveScreen.Amount));
        }

        [Fact]
        public void ASlaveTheMapDoesNotKnow_ShowsTheFullScreen()
        {
            Assert.Equal(SlaveScreen.Full, _slaves[7]);
        }

        [Fact]
        public void Set_KeepsEachSlavesScreen_AndNoneForgetsIt()
        {
            _slaves.Set(7, SlaveScreen.Recipes);
            _slaves.Set(8, SlaveScreen.Amount);
            _slaves.Set(8, SlaveScreen.Amount); // sent back by the server: no change

            Assert.Equal(SlaveScreen.Recipes, _slaves[7]);
            Assert.Equal(SlaveScreen.Amount, _slaves[8]);

            _slaves.Set(7, SlaveScreen.None);
            Assert.Single(_slaves.Entries);
        }
    }
}
