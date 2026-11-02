using System.Linq;
using FurnaceControlMod.Logic;
using Xunit;
using static FurnaceControl.Tests.Recipes;

namespace FurnaceControl.Tests
{
    public class BatchPlanTests
    {
        static double ToAdd(BatchPlan plan, string name) => plan.Needs.Single(need => need.Ingredient.Name == name).ToAdd;

        // One unit of reagent is one ingot (FurnaceBase.CreateIngots), so 200 solder is 100 iron and 100 lead.
        [Fact]
        public void Solder200_After75IronAnd25Lead_Needs25IronAnd75Lead()
        {
            var plan = BatchPlan.For(Solder, 200, new[] { Have(1, 75), Have(2, 25) });

            Assert.Equal(200, plan.Units);
            Assert.Equal(200, plan.Makes);
            Assert.Equal(25, ToAdd(plan, "Iron"));
            Assert.Equal(75, ToAdd(plan, "Lead"));
            Assert.Equal(50, plan.Ready); // 25 lead and 25 of the iron make 50 solder
            Assert.False(plan.Complete);
        }

        [Fact]
        public void NothingIn_NeedsTheWholeBatch()
        {
            var plan = BatchPlan.For(Solder, 100, Enumerable.Empty<Holding>());

            Assert.Equal(50, ToAdd(plan, "Iron"));
            Assert.Equal(50, ToAdd(plan, "Lead"));
            Assert.Equal(0, plan.Ready);
        }

        [Fact]
        public void AllIn_IsComplete()
        {
            var plan = BatchPlan.For(Solder, 100, new[] { Have(1, 50), Have(2, 50) });

            Assert.True(plan.Complete);
            Assert.Equal(100, plan.Ready);
        }

        [Fact]
        public void SuperAlloy_TakesFourUnitsAnIngot()
        {
            var plan = BatchPlan.For(Stellite, 25, Enumerable.Empty<Holding>());

            Assert.Equal(100, plan.Units);
            Assert.Equal(25, plan.Makes);
            Assert.Equal(50, ToAdd(plan, "Silicon"));
            Assert.Equal(25, ToAdd(plan, "Silver"));
            Assert.Equal(25, ToAdd(plan, "Cobalt"));
        }

        [Fact]
        public void QuarterRatios_RoundTheBatchUpToWholeUnits()
        {
            // 1 ingot is 4 units; 2 ingots, 8: each a whole number of silver and cobalt
            Assert.Equal(4, BatchPlan.Step(Stellite));
            Assert.Equal(4, BatchPlan.For(Stellite, 1, Enumerable.Empty<Holding>()).Units);
            Assert.Equal(2, BatchPlan.Step(Solder));
            Assert.Equal(2, BatchPlan.For(Solder, 1, Enumerable.Empty<Holding>()).Units); // 1 iron and 1 lead
        }

        [Fact]
        public void TooMuchOfOne_RaisesTheOthersAndTheBatch()
        {
            // 100 solder wants 50 iron; 60 went in, so 60 lead balances it, for 120 solder
            var plan = BatchPlan.For(Solder, 100, new[] { Have(1, 60), Have(2, 15) });

            Assert.Equal(120, plan.Units);
            Assert.Equal(120, plan.Makes);
            Assert.Equal(0, ToAdd(plan, "Iron"));
            Assert.Equal(45, ToAdd(plan, "Lead"));
        }

        [Fact]
        public void ReagentNotInTheRecipe_IsUnwanted()
        {
            var plan = BatchPlan.For(Solder, 10, new[] { Have(1, 10), Have(2, 10), Have(9, 3) });

            Assert.Single(plan.Unwanted);
            Assert.Equal(9, plan.Unwanted[0].Hash);
            Assert.False(plan.Complete);
        }

        [Fact]
        public void Ready_CountsOnlyCompleteSets()
        {
            // 7 silicon, 3 silver, 2 cobalt: 2 cobalt is 8 units' worth, but 8 units take 4 silicon, 2 silver, 2 cobalt
            var plan = BatchPlan.For(Stellite, 25, new[] { Have(3, 7), Have(4, 3), Have(5, 2) });

            Assert.Equal(2, plan.Ready);
        }

        [Fact]
        public void HoldingsOfOneReagent_AddUp()
        {
            // melted in, and waiting in the import slot
            var plan = BatchPlan.For(IronIngot, 50, new[] { Have(1, 20), Have(1, 10) });

            Assert.Equal(20, ToAdd(plan, "Iron"));
            Assert.Equal(30, plan.Ready);
        }

        [Fact]
        public void IsDone_WhenTheIngotFormsAndEveryUnitIsIn()
        {
            Assert.True(BatchPlan.IsDone(Solder, 200, Solder.OutputHash, 200));
            Assert.False(BatchPlan.IsDone(Solder, 200, Solder.OutputHash, 199));
            Assert.False(BatchPlan.IsDone(Solder, 200, 0, 200)); // out of the recipe's range, or the ratio's off
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(1, 1)]
        [InlineData(500, 500)]
        [InlineData(501, 500)]
        public void Amount_IsClampedTo1To500(int typed, int clamped) => Assert.Equal(clamped, BatchPlan.ClampAmount(typed));

        [Fact]
        public void Ingots_FollowTheFurnace()
        {
            Assert.Equal(100, BatchPlan.Ingots(Solder, 100.4)); // floor, then the scale
            Assert.Equal(25, BatchPlan.Ingots(Stellite, 100));
            Assert.Equal(2, BatchPlan.Ingots(Stellite, 6));  // 1.5: halves round to even, as Mathf.RoundToInt does
            Assert.Equal(2, BatchPlan.Ingots(Stellite, 10)); // 2.5
        }

        [Fact]
        public void WholeRatio_IsTheSmallestWholeBatch()
        {
            Assert.Equal(new[] { 1, 1 }, BatchPlan.WholeRatio(Solder));
            Assert.Equal(new[] { 2, 1, 1 }, BatchPlan.WholeRatio(Stellite));
            Assert.Equal(new[] { 1 }, BatchPlan.WholeRatio(IronIngot));
        }
    }
}
