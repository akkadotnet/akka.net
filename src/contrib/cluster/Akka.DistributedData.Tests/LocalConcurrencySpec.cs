//-----------------------------------------------------------------------
// <copyright file="LocalConcurrencySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Immutable;
using Akka.Actor;
using Akka.Configuration;
using Xunit;
using Xunit.Abstractions;

namespace Akka.DistributedData.Tests
{
    [Collection("DistributedDataSpec")]
    public class LocalConcurrencySpec : Akka.TestKit.Xunit2.TestKit
    {
        public sealed class Updater : ReceiveActor, IWithUnboundedStash
        {
            public static readonly ORSetKey<string> Key = new ORSetKey<string>("key");
            public Updater()
            {
                var cluster = Cluster.Cluster.Get(Context.System);
                var replicator = DistributedData.Get(Context.System).Replicator;

                Receive<string>(s =>
                {
                    var update = Dsl.Update(Key, ORSet<string>.Empty, WriteLocal.Instance, old => old.Add(cluster.SelfUniqueAddress, s));
                    replicator.Tell(update);
                });
            }

            public IStash Stash { get; set; }
        }

        private readonly IActorRef _replicator;

        public LocalConcurrencySpec(ITestOutputHelper output)
            : base(ConfigurationFactory.ParseString(@"
              akka.actor.provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
              akka.remote.dot-netty.tcp.port = 0"),
            "LocalConcurrencySpec", output)
        {
            _replicator = DistributedData.Get(Sys).Replicator;
        }

        [Fact]
        public void Updates_from_same_node_should_be_possible_to_do_from_two_actors()
        {
            var updater1 = ActorOf(Props.Create<Updater>(), "updater1");
            var updater2 = ActorOf(Props.Create<Updater>(), "updater2");

            var b = ImmutableHashSet<string>.Empty.ToBuilder();
            for (int i = 1; i <= 100; i++)
            {
                var m1 = "a" + 1;
                var m2 = "b" + 1;
                updater1.Tell(m1);
                updater2.Tell(m2);

                b.Add(m1);
                b.Add(m2);
            }

            var expected = b.ToImmutable();
            AwaitAssert(() =>
            {
                _replicator.Tell(Dsl.Get(Updater.Key, ReadLocal.Instance));
                var msg = ExpectMsg<GetSuccess>();
                var elements = msg.Get(Updater.Key).Elements;
                Assert.Equal(expected, elements);
            });
        }
    }
}
