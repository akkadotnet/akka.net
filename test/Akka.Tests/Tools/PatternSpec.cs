using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Akka.Tests.Tools
{
    [TestClass]
    public class PatternSpec
    {
        [TestMethod]
        public void PatternMatch_should_not_throw_NullReferenceException()
        {
            object nullObj = null;
            nullObj.Match()
                .With<string>(str => { })
                .Default(m => {});
        }
    }
}
