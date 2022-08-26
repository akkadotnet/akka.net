//-----------------------------------------------------------------------
// <copyright file="ShardEntityFailureSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Sharding.Internal;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.Event;
using Akka.TestKit;
using Akka.Util;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Cluster.Sharding.Tests
{
    public class ShardEntityFailureSpec: AkkaSpec
    {
        private static readonly Config Config = ConfigurationFactory.ParseString(@"
                akka.loglevel = DEBUG
                akka.actor.provider = cluster
                akka.persistence.journal.plugin = ""akka.persistence.journal.inmem""
                akka.remote.dot-netty.tcp.port = 0")
            .WithFallback(ClusterSingletonManager.DefaultConfig())
            .WithFallback(ClusterSharding.DefaultConfig());
        
        private sealed class EntityEnvelope
        {
            public readonly long Id;
            public readonly object Payload;
            public EntityEnvelope(long id, object payload)
            {
                Id = id;
                Payload = payload;
            }
        }
        
        public ShardEntityFailureSpec(ITestOutputHelper helper) : base(Config, helper)
        {
        }

        private class ConstructorFailActor : ActorBase
        {
            private static bool _thrown;
            private readonly ILoggingAdapter _log = Context.GetLogger();

            public ConstructorFailActor()
            {
                if (!_thrown)
                {
                    _thrown = true;
                    throw new Exception("EXPLODING CONSTRUCTOR!");
                }
            }

            protected override bool Receive(object message)
            {
                _log.Info("Msg {0}", message);
                Sender.Tell($"ack {message}");
                return true;
            }
        }

        private class PreStartFailActor : ActorBase
        {
            private static bool _thrown;
            private readonly ILoggingAdapter _log = Context.GetLogger();

            protected override void PreStart()
            {
                base.PreStart();
                if (!_thrown)
                {
                    _thrown = true;
                    throw new Exception("EXPLODING PRE-START!");
                }
            }

            protected override bool Receive(object message)
            {
                _log.Info("Msg {0}", message);
                Sender.Tell($"ack {message}");
                return true;
            }
        }
        
        [Theory(DisplayName = "Persistent shard must recover from transient failures inside sharding entity constructor and PreStart method")]
        [MemberData(nameof(PropsFactory))]
        public async Task Persistent_Shard_must_recover_from_failing_entity(Props entityProp)
        {
            ExtractEntityId extractEntityId = message =>
            {
                switch (message)
                {
                    case EntityEnvelope env:
                        return (env.Id.ToString(), env.Payload);
                }
                return Option<(string, object)>.None;
            };

            ExtractShardId extractShardId = message =>
            {
                switch (message)
                {
                    case EntityEnvelope msg:
                        return msg.Id.ToString();
                }
                return null;
            };            

            var settings = ClusterShardingSettings.Create(Sys);
            settings = settings.WithTuningParameters(settings.TuningParameters.WithEntityRestartBackoff(1.Seconds()));
            var provider = new EventSourcedRememberEntitiesProvider("cats", settings);
            
            var props = Props.Create(() => new Shard(
                "cats",
                "shard-1",
                _ => entityProp,
                settings,
                extractEntityId,
                extractShardId,
                PoisonPill.Instance,
                provider
            ));

            Sys.EventStream.Subscribe<Error>(TestActor);

            var persistentShard = Sys.ActorOf(props);

            persistentShard.Tell(new EntityEnvelope(1, "Start"));

            // entity died here
            var err = ExpectMsg<Error>();
            err.Cause.Should().BeOfType<ActorInitializationException>();

            // Need to wait for the internal state to reset, else everything we sent will go to dead letter
            await AwaitConditionAsync(async () =>
            {
                persistentShard.Tell(Shard.GetCurrentShardState.Instance);
                var failedState = await ExpectMsgAsync<Shard.CurrentShardState>();
                return failedState.EntityIds.Count == 0;
            });

            // entity should be restarted when it received this message
            persistentShard.Tell(new EntityEnvelope(1, "Restarted"));
            ExpectMsg("ack Restarted");

            persistentShard.Tell(Shard.GetCurrentShardState.Instance);
            var state = ExpectMsg<Shard.CurrentShardState>();
            state.EntityIds.Count.Should().Be(1);
            state.EntityIds.First().Should().Be("1");
        }

        public static IEnumerable<object[]> PropsFactory()
        {
            yield return new object[] { Props.Create(() => new PreStartFailActor()) };
            yield return new object[] { Props.Create(() => new ConstructorFailActor()) };
        }
    }
}
