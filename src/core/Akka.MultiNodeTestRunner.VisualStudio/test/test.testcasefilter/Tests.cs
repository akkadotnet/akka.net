using Xunit;

namespace test.testcasefilter
{
    public class Tests
    {
        [Fact]
        [Trait("FilterCategory", "Exclude")]
        public void TestWithTraitToFilterOn()
        {
            Assert.True(false);
        }

        [Fact]
        [Trait("FilterCategory", "Include")]
        public void TestWithTraitToFilterOff()
        {
            Assert.True(true);
        }
    }
}
