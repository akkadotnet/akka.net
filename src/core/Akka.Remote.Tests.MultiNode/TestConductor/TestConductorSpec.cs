//-----------------------------------------------------------------------
// <copyright file="TestConductorSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Dsl;
using Akka.Configuration;
using Akka.MultiNode.TestAdapter;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;
using Akka.TestKit;
using Xunit;

namespace Akka.Remote.Tests.MultiNode.TestConductor
{
    public class TestConductorSpecConfig : MultiNodeConfig
    {
        public RoleName Master { get; private set; }

        public RoleName Slave { get; private set; }

        public TestConductorSpecConfig()
        {
            Master = Role("master");
            Slave = Role("slave");
            // Pinned to CLASSIC remoting even under AKKA_MNTR_TRANSPORT=artery: this spec
            // exercises genuine token-bucket RATE throttling, which artery's blackhole-only
            // test-mode does not support (Pekko's artery does not either -- its TestConductorSpec
            // runs classic-only). CommonConfig sits above MultiNodeConfig's artery tier in the
            // fallback chain, so this override always wins.
            CommonConfig = ConfigurationFactory.ParseString("akka.remote.artery.enabled = off")
                .WithFallback(DebugConfig(true));
            TestTransport = true;
        }
    }

    public class TestConductorSpec : MultiNodeSpec
    {
        private readonly TestConductorSpecConfig _config;

        public TestConductorSpec() : this(new TestConductorSpecConfig()) { }

        protected TestConductorSpec(TestConductorSpecConfig config) : base(config, typeof(TestConductorSpec))
        {
            _config = config;
        }

        protected override int InitialParticipantsValueFactory => 2;

        private IActorRef _echo;

        protected async Task<IActorRef> GetEchoActorRef()
        {
            if (_echo == null)
            {
                Sys.ActorSelection(Node(_config.Master).Root / "user" / "echo").Tell(new Identify(null));
                _echo = (await ExpectMsgAsync<ActorIdentity>()).Subject;
            }
            return _echo;
        }

        [MultiNodeFact]
        public async Task ATestConductorMust()
        {
            await Enter_a_BarrierAsync();
            await Support_Throttling_of_Network_ConnectionsAsync();
        }

        public async Task Enter_a_BarrierAsync()
        {
            RunOn(() =>
            {
                Sys.ActorOf(c => c.ReceiveAny((m, ctx) =>
                {
                    TestActor.Tell(m);
                    ctx.Sender.Tell(m);
                }), "echo");
            }, _config.Master);

            await EnterBarrierAsync("name");
        }

        // The throttle is a token bucket with 1000 B capacity. A message larger than the capacity is never
        // admitted, so the filler's wire size (text plus envelope) must stay under 1000 B.
        private static readonly string Filler = new('f', 600);

        /// <summary>
        /// Sends two fillers ahead of the timed messages. A fresh bucket admits its first message without
        /// draining (it is created with a last-send time of 0), and the second filler drains most of the
        /// remaining tokens. The numbered messages that follow then run into the throttle on every run.
        /// </summary>
        private static void SendFillers(IActorRef echo)
        {
            echo.Tell(Filler);
            echo.Tell(Filler);
        }

        private async Task ExpectFillersAsync()
        {
            await ExpectMsgAsync(Filler, TimeSpan.FromSeconds(5));
            await ExpectMsgAsync(Filler, TimeSpan.FromSeconds(5));
        }

        public async Task Support_Throttling_of_Network_ConnectionsAsync()
        {
            await RunOnAsync(async () =>
            {
                // start remote network connection so that it can be throttled
                (await GetEchoActorRef()).Tell("start");
            }, _config.Slave);

            await ExpectMsgAsync("start");

            await RunOnAsync(async () =>
            {
                await TestConductor.ThrottleAsync(_config.Slave, _config.Master, ThrottleTransportAdapter.Direction.Send, 0.01f);
            }, _config.Master);

            await EnterBarrierAsync("throttled_send");

            await RunOnAsync(async () =>
            {
                var echo = await GetEchoActorRef();
                SendFillers(echo);
                foreach (var i in Enumerable.Range(0, 10))
                {
                    echo.Tell(i);
                }
            }, _config.Slave);

            // Start timing only after the burst is through, so the bounds depend on the throttle rate
            // and not on barrier skew between the nodes.
            await ExpectFillersAsync();

            // Only the bytes beyond the bucket's remaining tokens are delayed, so the min is a fudged value:
            // messages have a different size in Akka.NET than on the JVM.
            await WithinAsync(TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(5), async () =>
            {
                await ExpectMsgAsync(0);
                (await ReceiveNAsync(9).ToListAsync()).ShouldOnlyContainInOrder(Enumerable.Range(1, 9).Cast<object>().ToArray());
            });

            await EnterBarrierAsync("throttled_send2");
            await RunOnAsync(async () =>
            {
                await TestConductor.ThrottleAsync(_config.Slave, _config.Master, ThrottleTransportAdapter.Direction.Send, -1);
                await TestConductor.ThrottleAsync(_config.Slave, _config.Master, ThrottleTransportAdapter.Direction.Receive, 0.01F);
            }, _config.Master);

            await EnterBarrierAsync("throttled_recv");

            await RunOnAsync(async () =>
            {
                var echo = await GetEchoActorRef();
                SendFillers(echo);
                foreach (var i in Enumerable.Range(10, 10))
                {
                    echo.Tell(i);
                }
            }, _config.Slave);

            // Both nodes see the fillers; only the slave's inbound side is throttled.
            await ExpectFillersAsync();

            var minMax = IsNode(_config.Master)
                ? (TimeSpan.Zero, TimeSpan.FromMilliseconds(500))
                : (TimeSpan.FromSeconds(0.3), TimeSpan.FromSeconds(5));

            await WithinAsync(minMax.Item1, minMax.Item2, async () =>
            {
                await ExpectMsgAsync(10);
                (await ReceiveNAsync(9).ToListAsync()).ShouldOnlyContainInOrder(Enumerable.Range(11, 9).Cast<object>().ToArray());
            });

            await EnterBarrierAsync("throttled_recv2");

            await RunOnAsync(async () =>
            {
                await TestConductor.ThrottleAsync(_config.Slave, _config.Master, ThrottleTransportAdapter.Direction.Receive, -1);
            }, _config.Master);

            await EnterBarrierAsync("after");
        }
    }
}
