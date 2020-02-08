//-----------------------------------------------------------------------
// <copyright file="ReplicatorSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster;
using Akka.Configuration;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.DistributedData.Tests
{
    public class ReplicatorSpecs : AkkaSpec
    {
        public static readonly Config SpecConfig;

        static ReplicatorSpecs()
        {
            SpecConfig = ConfigurationFactory.ParseString(@"
                akka.loglevel = DEBUG
                akka.actor.provider = cluster
                akka.remote.dot-netty.tcp.port = 0")
                .WithFallback(DistributedData.DefaultConfig());
        }

        private readonly ActorSystem _sys1;
        private readonly ActorSystem _sys2;
        private readonly ActorSystem _sys3;

        private readonly IActorRef _replicator1;
        private readonly IActorRef _replicator2;
        private readonly IActorRef _replicator3;

        private readonly TimeSpan _timeOut;
        private readonly WriteTo _writeTwo;
        private readonly WriteMajority _writeMajority;
        private readonly WriteAll _writeAll;
        private readonly ReadFrom _readTwo;
        private readonly ReadMajority _readMajority;
        private readonly ReadAll _readAll;

        private readonly ORDictionaryKey<string, Flag> KeyH = new ORDictionaryKey<string, Flag>("H");
        private readonly GSetKey<string> KeyI = new GSetKey<string>("I");

        public ReplicatorSpecs(ITestOutputHelper helper) : base(SpecConfig, helper)
        {
            _sys1 = Sys;
            _sys3 = ActorSystem.Create(Sys.Name, Sys.Settings.Config);
            _sys2 = ActorSystem.Create(Sys.Name, Sys.Settings.Config);

            _timeOut = Dilated(TimeSpan.FromSeconds(3.0));
            _writeTwo = new WriteTo(2, _timeOut);
            _writeMajority = new WriteMajority(_timeOut);
            _writeAll = new WriteAll(_timeOut);
            _readTwo = new ReadFrom(2, _timeOut);
            _readMajority = new ReadMajority(_timeOut);
            _readAll = new ReadAll(_timeOut);

            var settings = ReplicatorSettings.Create(Sys)
                .WithGossipInterval(TimeSpan.FromSeconds(1.0))
                .WithMaxDeltaElements(10);

            var props = Replicator.Props(settings);
            _replicator1 = _sys1.ActorOf(props, "replicator");
            _replicator2 = _sys2.ActorOf(props, "replicator");
            _replicator3 = _sys3.ActorOf(props, "replicator");
        }

        /// <summary>
        /// Reproduction spec for https://github.com/akkadotnet/akka.net/issues/4184
        /// </summary>
        [Fact]
        public async Task Bugfix_4184_Merge_ORDictionary()
        {
            await InitCluster();
            UpdateORDictionaryNode2And1();
        }

        private async Task InitCluster()
        {
            Cluster.Cluster.Get(_sys1).Join(Cluster.Cluster.Get(_sys1).SelfAddress); // coordinator will initially run on sys1
            await AwaitAssertAsync(() => Cluster.Cluster.Get(_sys1).SelfMember.Status.Should().Be(MemberStatus.Up));

            Cluster.Cluster.Get(_sys2).Join(Cluster.Cluster.Get(_sys1).SelfAddress);
            await WithinAsync(10.Seconds(), async () =>
            {
                await AwaitAssertAsync(() =>
                {
                    Cluster.Cluster.Get(_sys1).State.Members.Count.Should().Be(2);
                    Cluster.Cluster.Get(_sys1).State.Members.All(x => x.Status == MemberStatus.Up).Should().BeTrue();
                    Cluster.Cluster.Get(_sys2).State.Members.Count.Should().Be(2);
                    Cluster.Cluster.Get(_sys2).State.Members.All(x => x.Status == MemberStatus.Up).Should().BeTrue();
                });
            });

            Cluster.Cluster.Get(_sys3).Join(Cluster.Cluster.Get(_sys1).SelfAddress);
            await WithinAsync(10.Seconds(), async () =>
            {
                await AwaitAssertAsync(() =>
                {
                    Cluster.Cluster.Get(_sys1).State.Members.Count.Should().Be(3);
                    Cluster.Cluster.Get(_sys1).State.Members.All(x => x.Status == MemberStatus.Up).Should().BeTrue();
                    Cluster.Cluster.Get(_sys2).State.Members.Count.Should().Be(3);
                    Cluster.Cluster.Get(_sys2).State.Members.All(x => x.Status == MemberStatus.Up).Should().BeTrue();
                    Cluster.Cluster.Get(_sys3).State.Members.Count.Should().Be(3);
                    Cluster.Cluster.Get(_sys3).State.Members.All(x => x.Status == MemberStatus.Up).Should().BeTrue();
                });
            });
        }

        private void UpdateORDictionaryNode2And1()
        {
            var changedProbe = CreateTestProbe(_sys2);

            // subscribe to updates for KeyH, then update it with a replication factor of two
            _replicator2.Tell(Dsl.Subscribe(KeyH, changedProbe.Ref));
            _replicator2.Tell(Dsl.Update(KeyH, ORDictionary<string, Flag>.Empty, _writeTwo, x => x.SetItem(Cluster.Cluster.Get(_sys2), "a", Flag.False)));

            // receive local update
            changedProbe.ExpectMsg<Changed>(g => Equals(g.Key, KeyH)).Get(KeyH).Entries.SequenceEqual(ImmutableDictionary.CreateRange(new[]
            {
                new KeyValuePair<string, Flag>("a", Flag.False),
            })).ShouldBeTrue();

            // push update from node 1
            _replicator1.Tell(Dsl.Update(KeyH, ORDictionary<string, Flag>.Empty, _writeTwo, x => x.SetItem(Cluster.Cluster.Get(_sys1), "a", Flag.True)));

            // expect replication of update on node 2
            changedProbe.ExpectMsg<Changed>(g => Equals(g.Key, KeyH)).Get(KeyH).Entries.SequenceEqual(ImmutableDictionary.CreateRange(new[]
            {
                new KeyValuePair<string, Flag>("a", Flag.True)
            })).ShouldBeTrue();

            // add new value to dictionary from node 2
            _replicator2.Tell(Dsl.Update(KeyH, ORDictionary<string, Flag>.Empty, _writeTwo, x => x.SetItem(Cluster.Cluster.Get(_sys2), "b", Flag.True)));
            changedProbe.ExpectMsg<Changed>(g => Equals(g.Key, KeyH)).Get(KeyH).Entries.SequenceEqual(ImmutableDictionary.CreateRange(new[]
            {
                new KeyValuePair<string, Flag>("a", Flag.True),
                new KeyValuePair<string, Flag>("b", Flag.True)
            })).ShouldBeTrue();
        }

        // <summary>
        /// Reproduction spec for replicator duplicate publication.
        /// </summary>
        [Fact]
        public async Task Bugfix_Duplicate_Publish()
        {
            await InitCluster();
            await ReplicatorDuplicatePublish();
        }

        private async Task ReplicatorDuplicatePublish()
        {
            var p1 = CreateTestProbe(_sys1);
            var p2 = CreateTestProbe(_sys2);
            var p3 = CreateTestProbe(_sys3);

            var probes = new[] { p1, p2, p3 };

            // subscribe to updates on all 3 nodes
            _replicator1.Tell(Dsl.Subscribe(KeyI, p1.Ref));
            _replicator2.Tell(Dsl.Subscribe(KeyI, p2.Ref));
            _replicator3.Tell(Dsl.Subscribe(KeyI, p3.Ref));

            // update item on 2
            _replicator2.Tell(Dsl.Update(KeyI, GSet<string>.Empty, _writeTwo, a => a.Add("a")));

            Sys.Log.Info("Pushed change from sys2 for I");

            // wait for write to replicate to all 3 nodes
            Within(TimeSpan.FromSeconds(5), () =>
            {
                foreach(var p in probes)
                    p.ExpectMsg<Changed>(c => c.Get(KeyI).Elements.ShouldBe(ImmutableHashSet.Create("a")));
            });

            // create duplicate write on node 1
            Sys.Log.Info("Pushing change from sys1 for I");
            _replicator1.Tell(Dsl.Update(KeyI, GSet<string>.Empty, _writeTwo, a => a.Add("a")));
            

            // no probe should receive an update
            p2.ExpectNoMsg(TimeSpan.FromSeconds(1));
        }

        protected override void BeforeTermination()
        {
            Shutdown(_sys1);
            Shutdown(_sys3);
        }

    }
}
