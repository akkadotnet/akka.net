//-----------------------------------------------------------------------
// <copyright file="WithinSampleTest.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.TestKit.Xunit2;
using FluentAssertions;
using Xunit;

namespace DocsExamples.Testkit
{
    public class WithinSampleTest : TestKit
    {
#region WithinSample_0
        [Fact]
        public void WithinSample()
        {
            TestActor.Tell(42, ActorRefs.NoSender);
            Within(0.Milliseconds(), 1.Seconds(), () =>
            {
                Assert.Equal(42, ExpectMsg<int>());
            });
        }
#endregion WithinSample_0
    }
}
