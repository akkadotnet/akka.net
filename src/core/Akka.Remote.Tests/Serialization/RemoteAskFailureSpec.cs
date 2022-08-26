//-----------------------------------------------------------------------
// <copyright file="RemoteAskFailureSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;
using Xunit.Abstractions;
using static  FluentAssertions.FluentActions;

namespace Akka.Remote.Tests.Serialization
{
    public class RemoteAskFailureSpec: TestKit.Xunit2.TestKit
    {
        private static Config Config(int port) => @$"
akka.actor.ask-timeout = 5s
akka.actor.serializers.hyperion = ""Akka.Serialization.HyperionSerializer, Akka.Serialization.Hyperion""
akka.actor.serialization-bindings {{
  ""System.Object"" = hyperion
}}
akka.actor.provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
akka.remote.dot-netty.tcp.hostname = localhost
akka.remote.dot-netty.tcp.port = {port}";

        private ActorSystem _sys1;
        private ActorSystem _sys2;
        
        public RemoteAskFailureSpec(ITestOutputHelper output) : base(Config(12552), nameof(RemoteAskFailureSpec), output)
        {
        }

        public override Task InitializeAsync()
        {
            _sys1 = Sys;
            _sys2 = ActorSystem.Create(Sys.Name, Config(19999));
            InitializeLogger(_sys2);
            return Task.CompletedTask;
        }

        protected override async Task AfterAllAsync()
        {
            await ShutdownAsync(_sys2);
            await base.AfterAllAsync();
        }

        [Fact(DisplayName = "Ask operation using selector to a remote actor that expects Status.Failure should return Status.Failure")]
        public async Task RemoteSelectorFailureMessageTest()
        {
            _sys2.ActorOf(Props.Create(() => new FailActor()), "fail");
            var selector = _sys1.ActorSelection($"akka.tcp://{_sys2.Name}@localhost:19999/user/fail");

            var fail = await selector.Ask<Status.Failure>("doesn't matter");
            fail.Cause.Should().NotBeNull();
            fail.Cause.Should().BeOfType<TestException>();
            fail.Cause.Message.Should().Be("BOOM");
        }
        
        [Fact(DisplayName = "Ask operation using selector to a remote actor that does not expects Status.Failure should throw the exception cause")]
        public async Task RemoteSelectorFailureExceptionTest()
        {
            _sys2.ActorOf(Props.Create(() => new FailActor()), "fail");
            var selector = _sys1.ActorSelection($"akka.tcp://{_sys2.Name}@localhost:19999/user/fail");

            (await Awaiting(async () =>
            {
                await selector.Ask<string>("doesn't matter");
            }).Should().ThrowAsync<TestException>()).And.Message.Should().Be("BOOM");
        }
        
        [Fact(DisplayName = "Ask operation using deployment to a remote actor that expects Status.Failure should return Status.Failure")]
        public async Task RemoteDeploymentFailureMessageTest()
        {
            var sys2Address = RARP.For(_sys2).Provider.DefaultAddress;
            var remote = _sys1.ActorOf(
                Props.Create(() => new FailActor())
                    .WithDeploy(new Deploy(new RemoteScope(sys2Address))), 
                "fail");

            var fail = await remote.Ask<Status.Failure>("doesn't matter");
            fail.Cause.Should().NotBeNull();
            fail.Cause.Should().BeOfType<TestException>();
            fail.Cause.Message.Should().Be("BOOM");
        }
        
        [Fact(DisplayName = "Ask operation using deployment to a remote actor that does not expects Status.Failure should throw the exception cause")]
        public async Task RemoteDeploymentFailureExceptionTest()
        {
            var sys2Address = RARP.For(_sys2).Provider.DefaultAddress;
            var remote = _sys1.ActorOf(
                Props.Create(() => new FailActor())
                    .WithDeploy(new Deploy(new RemoteScope(sys2Address))), 
                "fail");

            (await Awaiting(async () =>
            {
                await remote.Ask<string>("doesn't matter");
            }).Should().ThrowAsync<TestException>()).And.Message.Should().Be("BOOM");
        }
        
        private class FailActor: ReceiveActor
        {
            private readonly ILoggingAdapter _log;
            public FailActor()
            {
                _log = Context.GetLogger();
                
                ReceiveAsync<string>(async msg =>
                {
                    await Task.Delay(2.Seconds());
                    Sender.Tell(new Status.Failure(new TestException("BOOM")));
                });
            }

            protected override void PreStart()
            {
                base.PreStart();
                _log.Info($"{nameof(FailActor)} actor created in [{RARP.For(Context.System).Provider.DefaultAddress}]");
            }
        }
        
        private class TestException: Exception
        {
            public TestException()
            {
            }

            protected TestException(SerializationInfo info, StreamingContext context) : base(info, context)
            {
            }

            public TestException(string message) : base(message)
            {
            }

            public TestException(string message, Exception innerException) : base(message, innerException)
            {
            }
        }
    }
}
