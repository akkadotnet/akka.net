﻿//-----------------------------------------------------------------------
// <copyright file="ProxyShardingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Globalization;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Pattern;
using Xunit;

namespace Akka.Cluster.Sharding.Tests
{
    public class SupervisionSpec : TestKit.Xunit2.TestKit
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
                        Context.Stop(Self);
                        break;
                    case "hello":
                        Sender.Tell(new Response(Self));
                        break;
                    case StopMessage _:
                        Log.Info("Received stop from region");
                        Context.Parent.Tell(PoisonPill.Instance);
                        break;
                }
            }
        }

        #endregion

        private readonly ExtractEntityId _extractEntityId = message =>
            message is Msg msg ? new Tuple<string, object>(msg.Id.ToString(), msg.Message) : null;

        private readonly ExtractShardId _extractShard = message =>
            message is Msg msg ? (msg.Id % 2).ToString(CultureInfo.InvariantCulture) : null;

        public SupervisionSpec() : base(GetConfig())
        { }

        public static Config GetConfig()
        {
            return ConfigurationFactory.ParseString(@"akka.actor.provider = cluster
                                                      akka.loglevel = INFO
                                                      akka.remote.dot-netty.tcp.port = 0")
                .WithFallback(ClusterSharding.DefaultConfig());
        }

        [Fact]
        public void SupervisionSpec_for_a_sharded_actor_must_allow_passivation()
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

            // This would fail before as sharded actor would be stuck passivating
            region.Tell(new Msg(10, "hello"));
            ExpectMsg<Response>(TimeSpan.FromSeconds(20));
        }
    }
}