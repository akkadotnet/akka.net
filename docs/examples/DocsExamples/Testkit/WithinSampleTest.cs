// <copyright file="WithinSampleTest.cs" company="Copaco B.V.">
//        Copyright (c) 2015 - 2017 All Right Reserved
//        Author: Arjen Smits
// </copyright>

using Akka.Actor;
using Akka.TestKit.Xunit2;
using FluentAssertions;
using Xunit;

namespace DocsExamples.Testkit
{
    public class WithinSampleTest : TestKit
    {
        [Fact]
        public void WithinSample()
        {
            TestActor.Tell(42, ActorRefs.NoSender);
            Within(0.Milliseconds(), 1.Seconds(), () =>
            {
                Assert.Equal(42, ExpectMsg<int>());
            });
        }
    }
}