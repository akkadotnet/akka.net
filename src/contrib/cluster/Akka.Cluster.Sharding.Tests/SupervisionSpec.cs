//-----------------------------------------------------------------------
// <copyright file="SupervisionSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Globalization;
using System.Threading;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Pattern;
using Akka.TestKit;
using Akka.Util;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Cluster.Sharding.Tests
{
    public class SupervisionSpec : AkkaSpec
    {
        #region Protocol

        internal class Msg
        {
            public long Id { get; }
            public object Message { get; }

            public Msg(long id, object message)
            {
                Id = id;
                Message = message;
            }
        }

        internal class Response
        {
            public IActorRef Self { get; }

            public Response(IActorRef self)
            {
                Self = self;
            }
        }

        internal class StopMessage
        {
            public static readonly StopMessage Instance = new StopMessage();
            private StopMessage() { }
        }

        internal class PassivatingActor : UntypedActor
        {
            public ILoggingAdapter Log { get; } = Context.GetLogger();

            protected override void PreStart()
            {
                Log.Info("Starting");
                base.PreStart();
            }

            protected override void PostStop()
            {
                Log.Info("Stopping");
                base.PostStop();
            }

            protected override void OnReceive(object message)
            {
                switch (message)
                {
                    case "passivate":
                        Log.Info("Passivating");
                        Context.Parent.Tell(new Passivate(StopMessage.Instance));
                        // simulate another message causing a stop before the region sends the stop message
                        // e.g. a persistent actor having a persist failure while processing the next message
                        // note that this means the StopMessage will go to dead letters
                        Context.Stop(Self);
                        break;
                    case "hello":
                        Sender.Tell(new Response(Self));
                        break;
                    case StopMessage _:
                        // note that we never see this because we stop early
                        Log.Info("Received stop from region");
                        Context.Parent.Tell(PoisonPill.Instance);
                        break;
                }
            }
        }

        #endregion

        private readonly ExtractEntityId _extractEntityId = message =>
            message is Msg msg ? (msg.Id.ToString(), msg.Message) : Option<(string, object)>.None;

        private readonly ExtractShardId _extractShard = message =>
            message is Msg msg ? (msg.Id % 2).ToString(CultureInfo.InvariantCulture) : null;

        private static Config SpecConfig =>
            ConfigurationFactory.ParseString(@"
                akka.actor.provider = cluster
                akka.loglevel = DEBUG
                akka.remote.dot-netty.tcp.port = 0
                akka.cluster.sharding.verbose-debug-logging = on
                akka.cluster.sharding.fail-on-invalid-entity-state-transition = on")
                .WithFallback(ClusterSharding.DefaultConfig());

        public SupervisionSpec(ITestOutputHelper output) : base(SpecConfig, output)
        {
        }

        [Fact]
        public void SupervisionSpec_for_a_sharded_actor_deprecated_must_allow_passivation_and_early_stop()
        {
            var supervisedProps = BackoffSupervisor.Props(Backoff.OnStop(
                Props.Create<PassivatingActor>(),
                "child",
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(30),
                0.2,
                -1).WithFinalStopMessage(message => message is StopMessage));

            Cluster.Get(Sys).Join(Cluster.Get(Sys).SelfAddress);

            var region = ClusterSharding.Get(Sys).Start(
                "passy",
                supervisedProps,
                ClusterShardingSettings.Create(Sys),
                _extractEntityId,
                _extractShard);

            region.Tell(new Msg(10, "hello"));
            var response = ExpectMsg<Response>(TimeSpan.FromSeconds(5));
            Watch(response.Self);

            region.Tell(new Msg(10, "passivate"));
            ExpectTerminated(response.Self);
            Thread.Sleep(200);

            // This would fail before as sharded actor would be stuck passivating
            region.Tell(new Msg(10, "hello"));
            ExpectMsg<Response>(TimeSpan.FromSeconds(20));
        }

        [Fact]
        public void SupervisionSpec_for_a_sharded_actor_must_allow_passivation_and_early_stop()
        {
            var supervisedProps = BackoffSupervisor.Props(Backoff.OnStop(
                Props.Create<PassivatingActor>(),
                "child",
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(30),
                0.2,
                -1).WithFinalStopMessage(message => message is StopMessage));

            Cluster.Get(Sys).Join(Cluster.Get(Sys).SelfAddress);

            var region = ClusterSharding.Get(Sys).Start(
                "passy",
                supervisedProps,
                ClusterShardingSettings.Create(Sys),
                _extractEntityId,
                _extractShard);

            region.Tell(new Msg(10, "hello"));
            var response = ExpectMsg<Response>(TimeSpan.FromSeconds(5));
            Watch(response.Self);

            // 1. passivation message is passed on from supervisor to shard (which starts buffering messages for the entity id)
            // 2. child stops
            // 3. the supervisor has or has not yet seen gotten the stop message back from the shard
            //   a. if has it will stop immediatel, and the next message will trigger the shard to restart it
            //   b. if it hasn't the supervisor will back off before restarting the child, when the
            //     final stop message `StopMessage` comes in from the shard it will stop itself
            // 4. when the supervisor stops the shard should start it anew and deliver the buffered messages
            region.Tell(new Msg(10, "passivate"));
            ExpectTerminated(response.Self);
            Thread.Sleep(200);

            region.Tell(new Msg(10, "hello"));
            ExpectMsg<Response>(TimeSpan.FromSeconds(20));
        }
    }
}
