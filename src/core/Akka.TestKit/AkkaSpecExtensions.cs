using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Akka.TestKit
{
    public static class AkkaSpecExtensions
    {
        public static void Then<T>(this T self, Action<T> body)
        {
            body(self);
        }

        public static void Then<T>(this T self, Action<T, T> body, T other)
        {
            body(other, self);
        }

        public static void ShouldBe<T>(this IEnumerable<T> self, IEnumerable<T> other)
        {
            Xunit.Assert.True(self.SequenceEqual(other), "Expected " + other.Select(i => string.Format("'{0}'", i)).Join(",") + " got " + self.Select(i => string.Format("'{0}'", i)).Join(","));
        }

        public static void ShouldBe<T>(this T self, T other, string message = null)
        {
            Xunit.Assert.Equal(self, other);
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

            Xunit.Assert.Equal(expected, actual);
        }
    }
}