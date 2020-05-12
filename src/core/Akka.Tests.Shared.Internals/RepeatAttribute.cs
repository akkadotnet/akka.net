using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Xunit.Sdk;

namespace Akka.Tests.Shared.Internals
{
    /// <summary>
    /// This is an internal utility to test flaky/racy unit tests. 
    /// It allows the test runner to run a single unit test repeatedly to test for flaky situations.
    /// 
    /// NOTE:
    /// Make sure that this attribute are _NOT_ used in the unit test when it is ready to be committed, 
    /// because it creates artificial load that can bind the CI/CD PR validation process.
    /// </summary>
    /// <example>
    /// // This will repeatedly run MyUnitTest 500 times
    /// // Note that you NEED to use [Theory], and the unit test requires a single integer parameter.
    /// [Theory]
    /// [Repeat(500)]
    /// public void MyUnitTest(int _)
    /// { }
    /// </example>
    public sealed class RepeatAttribute : DataAttribute
    {
        private readonly int _count;

        public RepeatAttribute(int count)
        {
            if (count < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(count),
                      "Repeat count must be greater than 0.");
            }
            _count = count;
        }

        public override IEnumerable<object[]> GetData(MethodInfo testMethod)
        {
            foreach(var x in Enumerable.Range(1, _count))
            {
                yield return new object[] { x };
            }
        }
    }
}
