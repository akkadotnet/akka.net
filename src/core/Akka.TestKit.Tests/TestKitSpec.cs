//-----------------------------------------------------------------------
// <copyright file="TestKitSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Actor.Dsl;
using Akka.Configuration;
using Xunit;
using Xunit.Abstractions;
using FluentAssertions;
using static FluentAssertions.FluentActions;

namespace Akka.TestKit.Tests
{
    public class TestKitSpec
    {
        private readonly ITestOutputHelper _output;

        public TestKitSpec(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact(DisplayName = "TestKit should accept arbitrary ActorSystem")]
        public void TestKitBaseTest()
        {
            using (var sys = ActorSystem.Create(nameof(TestKitSpec)))
            {
                var testkit = new TestKit.Xunit2.TestKit(sys, _output);
                var echoActor = testkit.Sys.ActorOf(c => c.ReceiveAny((m, ctx) => testkit.TestActor.Tell(m))); 
                Invoking(() =>
                {
                    echoActor.Tell("message");
                    var message = testkit.ExpectMsg<string>();
                    message.Should().Be("message");
                }).Should().NotThrow<ConfigurationException>();
            }
        }

        [Fact(DisplayName = "TestKit should accept ActorSystem with TestKit.DefaultConfig")]
        public void TestKitConfigTest()
        {
            using (var sys = ActorSystem.Create(nameof(TestKitSpec), TestKit.Xunit2.TestKit.DefaultConfig))
            {
                var testkit = new TestKit.Xunit2.TestKit(sys, _output);
                var echoActor = testkit.Sys.ActorOf(c => c.ReceiveAny((m, ctx) => testkit.TestActor.Tell(m))); 
                Invoking(() =>
                {
                    echoActor.Tell("message");
                    var message = testkit.ExpectMsg<string>();
                    message.Should().Be("message");
                }).Should().NotThrow<ConfigurationException>();
            }
        }
    }
}
