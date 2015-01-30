using System;

namespace Akka.Util
{
    /// <summary>
    /// Contains static methods for representing program contracts.
    /// </summary>
    public static class Guard
    {
        /// <summary>
        /// Checks for a condition;if the condition is false throws <see cref="System.ArgumentException"/> exception.
        /// </summary>
        /// <param name="condition">The conditional expression to test.</param>
        /// <param name="errorMessage">Exception message.</param>
        public static void Assert(bool condition, string errorMessage = "Expression should be true, but was false")
        {
            if (!condition)
                throw new ArgumentException(errorMessage);
        }
    }
}
