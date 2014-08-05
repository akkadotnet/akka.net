using Xunit;

namespace Akka.Tests
{
    public static class XAssert
    {
        /// <summary>
        /// Fails the test with the specified reason.
        /// </summary>
        public static void Fail(string reason)
        {
            Assert.True(false, reason);
        }

        /// <summary>
        /// Asserts that both arguments are the same reference.
        /// </summary>
        public static void Same<T>(T expected, T actual, string message)
        {
            Assert.True(ReferenceEquals(expected,actual),message);
        }

        public static void Equal<T>(T expected, T actual, string message)
        {
            Assert.True(Equals(expected, actual), message);   //TODO: Delegate to the equals in TestKit
        }
    }
}