using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Xunit.Sdk;

namespace Akka.Tests.Shared.Internals
{
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
