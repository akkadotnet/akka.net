using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

// ReSharper disable once CheckNamespace
namespace Akka.TestKit
{
    public static class AkkaSpecExtensions
    {
        public static void ShouldBe<T>(this IEnumerable<T> self, IEnumerable<T> other)
        {
            Assert.True(self.SequenceEqual(other), "Expected " + other.Select(i => string.Format("'{0}'", i)).Join(",") + " got " + self.Select(i => string.Format("'{0}'", i)).Join(","));
        }

        public static void ShouldBe<T>(this T self, T expected, string message = null)
        {
            Assert.Equal(expected, self);
        }

        public static void ShouldBeTrue(this bool b, string message = null)
        {
            Assert.True(b);
        }

        public static void ShouldBeFalse(this bool b, string message = null)
        {
            Assert.False(b);
        }

        public static void ShouldOnlyContainInOrder<T>(this IEnumerable<T> actual, params T[] expected)
        {
            ShouldBe(actual, (IEnumerable<T>)expected);
        }

        public static async Task ThrowsAsync<TException>(Func<Task> func)
        {
            var expected = typeof(TException);
            Type actual = null;
            try
            {
                await func();
            }
            catch (Exception e)
            {
                actual = e.GetType();
            }

            Assert.Equal(expected, actual);
        }
    }
}