using Xunit;

namespace Akka.Tests.Tools
{
    
    public class PatternSpec
    {
        [Fact]
        public void PatternMatch_should_not_throw_NullReferenceException()
        {
            object nullObj = null;
            nullObj.Match()
                .With<string>(str => { })
                .Default(m => {});
        }
    }
}
