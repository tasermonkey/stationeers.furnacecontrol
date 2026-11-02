using System.Linq;
using FurnaceControlMod.Logic;
using Xunit;

namespace FurnaceControl.Tests
{
    public class AmountEntryTests
    {
        readonly AmountEntry _entry = new AmountEntry();

        void Type(params int[] digits)
        {
            foreach (var digit in digits) _entry.Press(digit);
        }

        [Fact]
        public void Digits_MakeTheAmount()
        {
            Type(1, 2, 5);

            Assert.Equal("125", _entry.Text);
            Assert.Equal(125, _entry.Value);
            Assert.True(_entry.IsValid);
        }

        [Fact]
        public void AFourthDigit_IsIgnored()
        {
            Type(1, 2, 5, 7);

            Assert.Equal(125, _entry.Value);
        }

        [Fact]
        public void LeadingZeros_AreIgnored()
        {
            Type(0, 0, 4, 0);

            Assert.Equal("40", _entry.Text);
        }

        [Theory]
        [InlineData(new int[0], false)]   // nothing typed
        [InlineData(new[] { 5, 0, 0 }, true)]
        [InlineData(new[] { 5, 0, 1 }, false)] // over 500
        [InlineData(new[] { 1 }, true)]
        public void Valid_From1To500(int[] digits, bool valid)
        {
            Type(digits);

            Assert.Equal(valid, _entry.IsValid);
        }

        [Fact]
        public void Delete_TakesTheLastDigit_AndClear_TakesAll()
        {
            Type(1, 2, 5);
            _entry.Delete();
            Assert.Equal(12, _entry.Value);

            _entry.Clear();
            Assert.Equal("", _entry.Text);
            _entry.Delete(); // nothing left: no change
            Assert.Equal(0, _entry.Value);
        }

        [Fact]
        public void Set_StartsFromAChosenAmount()
        {
            _entry.Set(100);
            _entry.Delete();

            Assert.Equal(10, _entry.Value);
        }
    }

    public class RecipeLayoutTests
    {
        static RecipeInfo Recipe(string name, int reagents = 1, double scale = 1, bool ingot = true)
        {
            var ingredients = new Ingredient[reagents];
            for (var i = 0; i < reagents; i++) ingredients[i] = new Ingredient(i, "#" + i, 1.0 / reagents);
            return new RecipeInfo(name, name.GetHashCode(), ingredients, 300, 99999, 100, 99999, scale, ingot);
        }

        [Fact]
        public void Groups_MetalAlloySuperAlloyOther()
        {
            Assert.Equal(RecipeGroup.Metal, Recipe("Iron").Group);
            Assert.Equal(RecipeGroup.Alloy, Recipe("Solder", 2).Group);
            Assert.Equal(RecipeGroup.SuperAlloy, Recipe("Stellite", 3, .25).Group);
            Assert.Equal(RecipeGroup.Other, Recipe("Charcoal", ingot: false).Group);
        }

        [Fact]
        public void Cells_ByFamily_AToZ_EachFamilyFromANewRow()
        {
            var recipes = new[]
            {
                Recipe("Stellite", 3, .25), Recipe("Charcoal", ingot: false), Recipe("Silver"), Recipe("Copper"),
                Recipe("Iron"), Recipe("Solder", 2), Recipe("Astroloy", 3, .25),
            };

            var names = RecipeLayout.Cells(recipes, 3).Select(recipe => recipe?.Name).ToArray();

            Assert.Equal(new[]
            {
                "Copper", "Iron", "Silver",
                "Solder", null, null,
                "Astroloy", "Stellite", null,
                "Charcoal",
            }, names);
        }

        [Fact]
        public void Cells_AFullRow_NeedsNoGap()
        {
            var names = RecipeLayout.Cells(new[] { Recipe("Iron"), Recipe("Gold"), Recipe("Solder", 2) }, 2)
                .Select(recipe => recipe?.Name).ToArray();

            Assert.Equal(new[] { "Gold", "Iron", "Solder" }, names);
        }
    }

    public class UnitsTests
    {
        [Theory]
        [InlineData(101.3, "101 kPa")]
        [InlineData(999.4, "999 kPa")]
        [InlineData(2100, "2.1 MPa")]
        [InlineData(55000, "55 MPa")]
        [InlineData(-1, "-")]
        public void Pressure_InKPaThenMPa(double kPa, string shown) => Assert.Equal(shown, Units.Pressure(kPa));

        [Fact]
        public void Ranges_SayAndUpForNoLimit()
        {
            Assert.Equal("350-550 K", Units.KelvinRange(350, 550));
            Assert.Equal("1800 K and up", Units.KelvinRange(1800, 99999));
            Assert.Equal("10 MPa-20 MPa", Units.PressureRange(10000, 20000));
            Assert.Equal("1 MPa and up", Units.PressureRange(1000, 99999));
        }

        [Fact]
        public void Kelvin_Rounds_AndOffIsADash()
        {
            Assert.Equal("451 K", Units.Kelvin(450.6));
            Assert.Equal("-", Units.Kelvin(-1));
        }

        [Fact]
        public void Reagent_WholeUnlessNot()
        {
            Assert.Equal("25", Units.Reagent(25.01));
            Assert.Equal("2.5", Units.Reagent(2.5));
        }
    }
}
