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
            CommonConfig = DebugConfig(true);
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
                foreach(var i in Enumerable.Range(0, 10))
                {
                    (await GetEchoActorRef()).Tell(i);
                }
            }, _config.Slave);

            // fudged the value to 0.5,since messages are a different size in Akka.NET
            await WithinAsync(TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(2), async () =>
            {
                await ExpectMsgAsync(0, TimeSpan.FromMilliseconds(500));
                (await ReceiveNAsync(9).ToListAsync()).ShouldOnlyContainInOrder(Enumerable.Range(1,9).Cast<object>().ToArray());
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
                foreach (var i in Enumerable.Range(10, 10))
                {
                    (await GetEchoActorRef()).Tell(i);
                }
            }, _config.Slave);

            var minMax = IsNode(_config.Master)
                ? (TimeSpan.Zero, TimeSpan.FromMilliseconds(500))
                : (TimeSpan.FromSeconds(0.3), TimeSpan.FromSeconds(3));

            await WithinAsync(minMax.Item1, minMax.Item2, async () =>
            {
                await ExpectMsgAsync(10, TimeSpan.FromMilliseconds(500));
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
