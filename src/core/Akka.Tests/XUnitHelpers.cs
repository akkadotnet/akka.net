using Xunit;

namespace Akka.Tests
{
    public static class XAssert
    {
        /// <summary>
        /// Fails the the test with the specified reason.
        /// </summary>
        public static void Fail(string reason)
        {
            Assert.True(false, reason);
        }
    }
}