//-----------------------------------------------------------------------
// <copyright file="ReplicatorSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster;
using Akka.Configuration;
using Akka.Configuration;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using ConfigurationFactory = Akka.Configuration.ConfigurationFactory;

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

        private readonly PNCounterDictionaryKey<string> _keyC = new PNCounterDictionaryKey<string>("C");
        private readonly ORDictionaryKey<string, Flag> _keyH = new ORDictionaryKey<string, Flag>("H");
        private readonly GSetKey<string> _keyI = new GSetKey<string>("I");
        private readonly ORMultiValueDictionaryKey<string, string> _keyJ = new ORMultiValueDictionaryKey<string, string>("J");

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
            _replicator2.Tell(Dsl.Subscribe(_keyH, changedProbe.Ref));
            _replicator2.Tell(Dsl.Update(_keyH, ORDictionary<string, Flag>.Empty, _writeTwo, x => x.SetItem(Cluster.Cluster.Get(_sys2), "a", Flag.False)));

            // receive local update
            changedProbe.ExpectMsg<Changed>(g => Equals(g.Key, _keyH)).Get(_keyH).Entries.SequenceEqual(ImmutableDictionary.CreateRange(new[]
            {
                new KeyValuePair<string, Flag>("a", Flag.False),
            })).ShouldBeTrue();

            // push update from node 1
            _replicator1.Tell(Dsl.Update(_keyH, ORDictionary<string, Flag>.Empty, _writeTwo, x => x.SetItem(Cluster.Cluster.Get(_sys1), "a", Flag.True)));

            // expect replication of update on node 2
            changedProbe.ExpectMsg<Changed>(g => Equals(g.Key, _keyH)).Get(_keyH).Entries.SequenceEqual(ImmutableDictionary.CreateRange(new[]
            {
                new KeyValuePair<string, Flag>("a", Flag.True)
            })).ShouldBeTrue();

            // add new value to dictionary from node 2
            _replicator2.Tell(Dsl.Update(_keyH, ORDictionary<string, Flag>.Empty, _writeTwo, x => x.SetItem(Cluster.Cluster.Get(_sys2), "b", Flag.True)));
            changedProbe.ExpectMsg<Changed>(g => Equals(g.Key, _keyH)).Get(_keyH).Entries.SequenceEqual(ImmutableDictionary.CreateRange(new[]
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
            _replicator1.Tell(Dsl.Subscribe(_keyI, p1.Ref));
            _replicator2.Tell(Dsl.Subscribe(_keyI, p2.Ref));
            _replicator3.Tell(Dsl.Subscribe(_keyI, p3.Ref));

            // update item on 2
            _replicator2.Tell(Dsl.Update(_keyI, GSet<string>.Empty, _writeTwo, a => a.Add("a")));

            Sys.Log.Info("Pushed change from sys2 for I");

            // wait for write to replicate to all 3 nodes
            Within(TimeSpan.FromSeconds(5), () =>
            {
                foreach(var p in probes)
                    p.ExpectMsg<Changed>(c => c.Get(_keyI).Elements.ShouldBe(ImmutableHashSet.Create("a")));
            });

            // create duplicate write on node 1
            Sys.Log.Info("Pushing change from sys1 for I");
            _replicator1.Tell(Dsl.Update(_keyI, GSet<string>.Empty, _writeTwo, a => a.Add("a")));
            

            // no probe should receive an update
            p2.ExpectNoMsg(TimeSpan.FromSeconds(1));
        }

        /// <summary>
        /// Reproduction spec for https://github.com/akkadotnet/akka.net/issues/4198
        /// </summary>
        [Fact]
        public async Task Bugfix_4198_PNCounterDictionary_Merge()
        {
            await InitCluster();
            await PNCounterDictionary_Should_Merge();
        }

        private async Task PNCounterDictionary_Should_Merge()
        {
            var p1 = CreateTestProbe(_sys1);
            var p2 = CreateTestProbe(_sys2);
            var p3 = CreateTestProbe(_sys3);

            var probes = new[] { 
                (p1, _replicator1, Cluster.Cluster.Get(_sys1)), 
                (p2, _replicator2, Cluster.Cluster.Get(_sys2)), 
                (p3, _replicator3, Cluster.Cluster.Get(_sys3))
            };

            AwaitAssert(() =>
            {
                _replicator1.Tell(Dsl.GetReplicaCount, p1);
                p1.ExpectMsg(new ReplicaCount(3));
            });

            // kick off writes
            foreach (var i in Enumerable.Repeat(0, 10))
            {
                foreach (var (probe, replicator, cluster) in probes)
                {
                    replicator.Tell(Dsl.Update(_keyC, PNCounterDictionary<string>.Empty,
                        new WriteAll(_timeOut), x => x.Increment(cluster, "x")
                            .Increment(cluster, "y")), probe);
                    probe.ExpectMsg(new UpdateSuccess(_keyC, null));
                }
            }

            // perform read to ensure values are consistent

            Within(TimeSpan.FromSeconds(5), () =>
            {
                AwaitAssert(() => {
                    foreach (var (probe, replicator, _) in probes)
                    {
                        replicator.Tell(Dsl.Get(_keyC, new ReadAll(_timeOut)), probe);
                        probe.ExpectMsg<GetSuccess>().Get(_keyC).Entries["x"].ShouldBe(new BigInteger(30.0d));
                    }
                });
            });

            Sys.Log.Info("Done");
        }

        /// <summary>
        /// Reproduction spec for https://github.com/akkadotnet/akka.net/issues/4198
        /// </summary>
        [Fact]
        public async Task Bugfix_4302_ORMultiValueDictionary_Merge()
        {
            await InitCluster();
            await ORMultiValueDictionary_Should_Merge();
        }

        private async Task ORMultiValueDictionary_Should_Merge()
        {
            var changedProbe = CreateTestProbe(_sys2);

            // subscribe to updates for KeyH, then 
            _replicator2.Tell(Dsl.Subscribe(_keyJ, changedProbe.Ref));

            Within(TimeSpan.FromSeconds(10), () =>
            {
                AwaitAssert(() =>
                {
                    // update it with a replication factor of two
                    _replicator2.Tell(Dsl.Update(_keyJ, ORMultiValueDictionary<string, string>.Empty,
                        _writeTwo, x => x.AddItem(Cluster.Cluster.Get(_sys2), "a", "A")));

                    // receive local update
                    VerifyMultiValueDictionaryEntries(
                        ImmutableDictionary.CreateRange(
                        new[] {
                    new KeyValuePair<string, IImmutableSet<string>>("a", ImmutableHashSet.Create("A")),
                        }),
                        changedProbe.ExpectMsg<Changed>(g => Equals(g.Key, _keyJ)).Get(_keyJ).Entries);
                });
            });

            Within(TimeSpan.FromSeconds(10), () =>
            {
                AwaitAssert(() =>
                {
                    // push update from node 1
                    // add item
                    _replicator1.Tell(Dsl.Update(_keyJ, ORMultiValueDictionary<string, string>.Empty,
                        _writeTwo, x => x.AddItem(Cluster.Cluster.Get(_sys1), "a", "A1")));

                    // expect replication of update on node 2
                    VerifyMultiValueDictionaryEntries(
                        ImmutableDictionary.CreateRange(
                        new[] {
                    new KeyValuePair<string, IImmutableSet<string>>("a", ImmutableHashSet.Create(new []{"A1", "A" })),
                        }),
                        changedProbe.ExpectMsg<Changed>(g => Equals(g.Key, _keyJ)).Get(_keyJ).Entries);
                });
            });

            Within(TimeSpan.FromSeconds(10), () =>
            {
                AwaitAssert(() =>
                {
                    // remove item
                    _replicator1.Tell(Dsl.Update(_keyJ, ORMultiValueDictionary<string, string>.Empty,
                        _writeTwo, x => x.RemoveItem(Cluster.Cluster.Get(_sys1), "a", "A")));
                    VerifyMultiValueDictionaryEntries(ImmutableDictionary.CreateRange(
                        new[] {
                    new KeyValuePair<string, IImmutableSet<string>>("a", ImmutableHashSet.Create("A1")),
                        }),
                        changedProbe.ExpectMsg<Changed>(g => Equals(g.Key, _keyJ)).Get(_keyJ).Entries);
                });
            });

            Within(TimeSpan.FromSeconds(10), () =>
            {
                AwaitAssert(() =>
                {
                    // replace item
                    _replicator1.Tell(Dsl.Update(_keyJ, ORMultiValueDictionary<string, string>.Empty,
                        _writeTwo, x => x.ReplaceItem(Cluster.Cluster.Get(_sys1), "a", "A1", "A")));
                    VerifyMultiValueDictionaryEntries(ImmutableDictionary.CreateRange(
                        new[] {
                    new KeyValuePair<string, IImmutableSet<string>>("a", ImmutableHashSet.Create("A")),
                        }),
                        changedProbe.ExpectMsg<Changed>(g => Equals(g.Key, _keyJ)).Get(_keyJ).Entries);
                });
            });

            Within(TimeSpan.FromSeconds(10), () =>
            {
                AwaitAssert(() =>
                {
                    // add new value to dictionary from node 2
                    _replicator2.Tell(Dsl.Update(_keyJ, ORMultiValueDictionary<string, string>.Empty,
                        _writeTwo, x => x.SetItems(Cluster.Cluster.Get(_sys2), "b", ImmutableHashSet.Create("B"))));

                    VerifyMultiValueDictionaryEntries(ImmutableDictionary.CreateRange(
                        new[] {
                    new KeyValuePair<string, IImmutableSet<string>>("a", ImmutableHashSet.Create("A")),
                    new KeyValuePair<string, IImmutableSet<string>>("b", ImmutableHashSet.Create("B")),
                        }),
                        changedProbe.ExpectMsg<Changed>(g => Equals(g.Key, _keyJ)).Get(_keyJ).Entries);
                });
            });
        }

        private void VerifyMultiValueDictionaryEntries(
            IImmutableDictionary<string, IImmutableSet<string>> expected,
            IImmutableDictionary<string, IImmutableSet<string>> entries)
        {
            expected.Count.Should().Equals(entries.Count);
            foreach (var kvp in entries)
            {
                expected.ContainsKey(kvp.Key).Should().BeTrue();
                expected.Values.Count().Should().Equals(expected[kvp.Key].Count());
                foreach (var value in kvp.Value)
                {
                    expected[kvp.Key].Contains(value).Should().BeTrue();
                }
            }
        }

        protected override void BeforeTermination()
        {
            Shutdown(_sys1);
            Shutdown(_sys3);
        }

    }
}
