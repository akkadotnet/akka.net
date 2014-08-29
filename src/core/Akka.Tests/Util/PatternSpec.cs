using Akka.Util;
using Xunit;

namespace Akka.Tests.Util
{
    public class PatternSpec
    {

        class A { }

        class B : A { }

        [Fact]
        public void PatternMatch_should_not_throw_NullReferenceException()
        {
            object nullObj = null;
            nullObj.Match()
                .With<string>(str => { })
                .Default(m => {});
        }

        [Fact]
        public void PatternBuilder_should_not_throw_NullReferenceException()
        {
            object nullObj = null;
            var pattern = PatternBuilder.Match()
                .With<string>(str => { })
                .Default(m => { })
                .Compile();
            var matched = pattern(nullObj);
        }

        [Fact]
        public void PatternBuilder_should_match_correct_type()
        {
            string message = "Message";
            bool matched = false;

            var pattern = PatternBuilder.Match()
                .With<string>(s => matched = true)
                .Compile();

            var handled = pattern(message);
            Assert.True(matched);
            Assert.True(handled);
        }

        [Fact]
        public void PatternBuilder_should_not_match_incorrect_type()
        {
            string message = "Message";
            bool matched = false;

            var pattern = PatternBuilder.Match()
                .With<int>(i => matched = true)
                .Compile();

            var handled = pattern(message);
            Assert.False(matched);
            Assert.False(handled);
        }

        [Fact]
        public void PatternBuilder_should_handle_default()
        {
            string message = "Message";
            bool matched = false;

            var pattern = PatternBuilder.Match()
                .With<int>(i => matched = true)
                .Default(m => { })
                .Compile();

            var handled = pattern(message);
            Assert.True(handled);
            Assert.False(matched);
        }

        [Fact]
        public void PatternBuilder_should_match_multiple_matched_patterns()
        {
            var testObjectB = new B();
            bool matchedA = false;
            bool matchedB = false;

            var pattern = PatternBuilder.Match()
                .With<A>(m => matchedA = true)
                .With<B>(m => matchedB = true)
                .Compile();

            var handled = pattern(testObjectB);

            Assert.True(matchedA);
            Assert.True(matchedB);
            Assert.True(handled);
        }
    }
}
