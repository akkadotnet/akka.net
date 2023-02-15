//-----------------------------------------------------------------------
// <copyright file="ThrottlerTransportAdapterSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Remote.Transport;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using Akka.TestKit.Internal;
using Akka.TestKit.Internal.StringMatcher;
using Akka.TestKit.TestEvent;
using Akka.Util;
using Akka.Util.Internal;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Remote.Tests.Transport
{
    public class ThrottlerTransportAdapterSpec : AkkaSpec
    {
        #region Setup / Config

        private static Config ThrottlerTransportAdapterSpecConfig
        {
            get
            {
                return ConfigurationFactory.ParseString(@"
                akka {
                  loglevel = ""DEBUG""
                  stdout-loglevel = ""DEBUG""
                  test.single-expect-default = 6s #to help overcome issues with gated connections
                  actor.provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
                  remote.dot-netty.tcp.hostname = ""localhost""
                  remote.log-remote-lifecycle-events = off
                  remote.retry-gate-closed-for = 1 s
                  remote.transport-failure-detector.heartbeat-interval = 1 s
                  remote.transport-failure-detector.acceptable-heartbeat-pause = 3 s
                  remote.dot-netty.tcp.applied-adapters = [""trttl""]
                  remote.dot-netty.tcp.port = 0
                }");
            }
        }

        private const int PingPacketSize = 350;
        private const int MessageCount = 15;
        private const int BytesPerSecond = 700;
        private const long TotalTime = (MessageCount * PingPacketSize) / BytesPerSecond;

        public class ThrottlingTester : ReceiveActor
        {
            private readonly IActorRef _remoteRef;
            private readonly IActorRef _controller;

            private int _received = 0;
            private int _messageCount = MessageCount;
            private long _startTime = 0L;

            public ThrottlingTester(IActorRef remoteRef, IActorRef controller)
            {
                _remoteRef = remoteRef;
                _controller = controller;

                Receive<string>(s => s.Equals("start"), s =>
                {
                    Self.Tell("sendNext");
                    _startTime = MonotonicClock.GetNanos();
                });

                Receive<string>(s => s.Equals("sendNext") && _messageCount > 0, s =>
                {
                    _remoteRef.Tell("ping");
                    Self.Tell("sendNext");
                    _messageCount--;
                });

                Receive<string>(s => s.Equals("pong"), s =>
                {
                    _received++;
                    if (_received >= MessageCount)
                        _controller.Tell(MonotonicClock.GetNanos() - _startTime);
                });
            }

            public sealed class Lost : IEquatable<Lost>
            {
                public Lost(string msg)
                {
                    Msg = msg;
                }

                public string Msg { get; }

                public bool Equals(Lost other)
                {
                    if (ReferenceEquals(null, other)) return false;
                    if (ReferenceEquals(this, other)) return true;
                    return string.Equals(Msg, other.Msg);
                }

                public override bool Equals(object obj)
                {
                    if (ReferenceEquals(null, obj)) return false;
                    if (ReferenceEquals(this, obj)) return true;
                    return obj is Lost lost && Equals(lost);
                }

                public override int GetHashCode()
                {
                    return Msg?.GetHashCode() ?? 0;
                }

                public override string ToString()
                {
                    return GetType() + ": " + Msg;
                }
            }
        }

        public class Echo : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                var str = message as string;
                if(!string.IsNullOrEmpty(str) && string.Equals(str, "ping"))
                    Sender.Tell("pong");
                else
                    Sender.Tell(message);
            }
        }

        private readonly ActorSystem _systemB;
        private readonly IActorRef _remote;

        private TimeSpan DefaultTimeout => Dilated(TestKitSettings.DefaultTimeout);
            
        private RootActorPath RootB
            => new RootActorPath(_systemB.AsInstanceOf<ExtendedActorSystem>().Provider.DefaultAddress);

        private async Task<IActorRef> Here()
        {
            var identity = await Sys.ActorSelection(RootB / "user" / "echo").Ask<ActorIdentity>(new Identify(null))
                .ShouldCompleteWithin(DefaultTimeout);
            return identity.Subject;
        }

        private async Task<bool> Throttle(ThrottleTransportAdapter.Direction direction, ThrottleMode mode)
        {
            var rootBAddress = new Address("akka", "systemB", "localhost", RootB.Address.Port.Value);
            var transport =
                Sys.AsInstanceOf<ExtendedActorSystem>().Provider.AsInstanceOf<RemoteActorRefProvider>().Transport;
            
            return await transport.ManagementCommand(new SetThrottle(rootBAddress, direction, mode))
                .ShouldCompleteWithin(DefaultTimeout);
        }

        private async Task<bool> Disassociate()
        {
            var rootBAddress = new Address("akka", "systemB", "localhost", RootB.Address.Port.Value);
            var transport =
                Sys.AsInstanceOf<ExtendedActorSystem>().Provider.AsInstanceOf<RemoteActorRefProvider>().Transport;
            
            return await transport.ManagementCommand(new ForceDisassociate(rootBAddress))
                .ShouldCompleteWithin(DefaultTimeout);
        }

        #endregion

        public ThrottlerTransportAdapterSpec(ITestOutputHelper output)
            : base(ThrottlerTransportAdapterSpecConfig, output)
        {
            _systemB = ActorSystem.Create("systemB", Sys.Settings.Config);
            _remote = _systemB.ActorOf(Props.Create<Echo>(), "echo");
        }

        #region Tests

        [Fact]
        public async Task ThrottlerTransportAdapter_must_maintain_average_message_rate()
        {
            await Throttle(
                    ThrottleTransportAdapter.Direction.Send,
                    new Remote.Transport.TokenBucket(PingPacketSize * 4, BytesPerSecond, 0, 0))
                .ShouldCompleteWithin(true, TimeSpan.FromSeconds(3));

            var here = await Here();
            var tester = Sys.ActorOf(Props.Create(() => new ThrottlingTester(here, TestActor)));
            tester.Tell("start");

            var time = TimeSpan.FromTicks(await ExpectMsgAsync<long>(TimeSpan.FromSeconds(TotalTime + 12))).TotalSeconds;
            Log.Warning("Total time of transmission: {0}", time);
            time.Should().BeGreaterThan(TotalTime - 12);

            await Throttle(ThrottleTransportAdapter.Direction.Send, Unthrottled.Instance)
                .ShouldCompleteWithin(true, TimeSpan.FromSeconds(3));
        }

        [Fact]
        public async Task ThrottlerTransportAdapter_must_survive_blackholing()
        {
            
            var here = await Here();
            here.Tell(new ThrottlingTester.Lost("BlackHole 1"));
            await ExpectMsgAsync(new ThrottlingTester.Lost("BlackHole 1"));

            MuteDeadLetters(typeof(ThrottlingTester.Lost));
            MuteDeadLetters(_systemB, typeof(ThrottlingTester.Lost));

            await Throttle(ThrottleTransportAdapter.Direction.Both, Blackhole.Instance)
                .ShouldCompleteWithin(true, 3.Seconds());

            here.Tell(new ThrottlingTester.Lost("BlackHole 2"));
            await ExpectNoMsgAsync(TimeSpan.FromSeconds(1));
            await Disassociate().ShouldCompleteWithin(true, TimeSpan.FromSeconds(3));
            await ExpectNoMsgAsync(TimeSpan.FromSeconds(1));

            await Throttle(ThrottleTransportAdapter.Direction.Both, Unthrottled.Instance)
                .ShouldCompleteWithin(true, TimeSpan.FromSeconds(3));

            // after we remove the Blackhole we can't be certain of the state
            // of the connection, repeat until success
            here.Tell(new ThrottlingTester.Lost("BlackHole 3"));
            await AwaitConditionAsync(async () =>
            {
                var received = await ReceiveOneAsync(TimeSpan.Zero);
                if (received != null && received.Equals(new ThrottlingTester.Lost("BlackHole 3")))
                    return true;

                here.Tell(new ThrottlingTester.Lost("BlackHole 3"));

                return false;
            }, TimeSpan.FromSeconds(15));

            here.Tell("Cleanup");
            await FishForMessageAsync(o => o.Equals("Cleanup"), TimeSpan.FromSeconds(5));
        }

        #endregion

        #region Cleanup 

        protected override async Task BeforeTerminationAsync()
        {
            EventFilter.Warning(start: "received dead letter").Mute();
            EventFilter.Warning(new Regex("received dead letter.*(InboundPayload|Disassociate)")).Mute();
            _systemB.EventStream.Publish(new Mute(new WarningFilter(new RegexMatcher(new Regex("received dead letter.*(InboundPayload|Disassociate)"))), 
                new ErrorFilter(typeof(EndpointException)),
                new ErrorFilter(new StartsWithString("AssociationError"))));
            await base.BeforeTerminationAsync();
        }

        protected override async Task AfterAllAsync()
        {
            await base.AfterAllAsync();
            await ShutdownAsync(_systemB);
        }

        #endregion
    }
}

