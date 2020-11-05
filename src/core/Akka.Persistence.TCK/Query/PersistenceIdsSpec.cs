//-----------------------------------------------------------------------
// <copyright file="PersistenceIdsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Query;
using Akka.Streams;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Akka.Util.Internal;
using Reactive.Streams;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.TCK.Query
{
    public abstract class PersistenceIdsSpec : Akka.TestKit.Xunit2.TestKit
    {
        protected ActorMaterializer Materializer { get; }

        protected virtual bool AllocatesAllPersistenceIDsPublisher => true;
        
        protected IReadJournal ReadJournal { get; set; }
        protected IActorRef SnapshotStore => Extension.SnapshotStoreFor(null);
        protected PersistenceExtension Extension { get; }

        private readonly TestProbe _senderProbe;

        protected PersistenceIdsSpec(Config config = null, string actorSystemName = null, ITestOutputHelper output = null)
            : base(config ?? Config.Empty, actorSystemName, output)
        {
            Materializer = Sys.Materializer();
            Extension = Persistence.Instance.Apply(Sys as ExtendedActorSystem);
            _senderProbe = CreateTestProbe();
        }

        [Fact]
        public void ReadJournal_should_implement_IAllPersistenceIdsQuery()
        {
            Assert.IsAssignableFrom<IPersistenceIdsQuery>(ReadJournal);
        }

        [Fact]
        public virtual void ReadJournal_AllPersistenceIds_should_find_new_events()
        {
            var queries = ReadJournal.AsInstanceOf<IPersistenceIdsQuery>();

            Setup("e", 1);
            Setup("f", 1);

            var source = queries.PersistenceIds();
            var probe = source.RunWith(this.SinkProbe<string>(), Materializer);

            probe.Within(TimeSpan.FromSeconds(10), () => probe.Request(5).ExpectNextUnordered("e", "f"));

            Setup("g", 1);
            probe.ExpectNext("g", TimeSpan.FromSeconds(10));
        }

        [Fact]
        public virtual void ReadJournal_AllPersistenceIds_should_find_new_events_after_demand_request()
        {
            var queries = ReadJournal.AsInstanceOf<IPersistenceIdsQuery>();

            Setup("h", 1);
            Setup("i", 1);

            var source = queries.PersistenceIds();
            var probe = source.RunWith(this.SinkProbe<string>(), Materializer);

            probe.Within(TimeSpan.FromSeconds(10), () =>
            {
                probe.Request(1).ExpectNext();
                return probe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
            });

            Setup("j", 1);
            probe.Within(TimeSpan.FromSeconds(10), () =>
            {
                probe.Request(5).ExpectNext();
                return probe.ExpectNext();
            });
        }

        [Fact]
        public virtual void ReadJournal_AllPersistenceIds_should_find_events_on_both_journal_and_snapshot_store()
        {
            var queries = ReadJournal.AsInstanceOf<IPersistenceIdsQuery>();

            WriteSnapshot("a", 2);
            WriteSnapshot("b", 2);
            WriteSnapshot("c", 2);
            Setup("d", 2);
            Setup("e", 2);
            Setup("f", 2);

            var source = queries.PersistenceIds();
            var probe = source.RunWith(this.SinkProbe<string>(), Materializer);

            var expectedUniqueList = new List<string>(){"a", "b", "c", "d", "e", "f"};

            probe.Within(TimeSpan.FromSeconds(10), () => probe.Request(3)
                .ExpectNextWithinSet(expectedUniqueList)
                .ExpectNextWithinSet(expectedUniqueList)
                .ExpectNextWithinSet(expectedUniqueList)
                .ExpectNoMsg(TimeSpan.FromMilliseconds(200)));

            probe.Within(TimeSpan.FromSeconds(10), () => probe.Request(3)
                .ExpectNextWithinSet(expectedUniqueList)
                .ExpectNextWithinSet(expectedUniqueList)
                .ExpectNextWithinSet(expectedUniqueList)
                .ExpectNoMsg(TimeSpan.FromMilliseconds(200)));
        }

        [Fact]
        public virtual void ReadJournal_AllPersistenceIds_should_only_deliver_what_requested_if_there_is_more_in_the_buffer()
        {
            var queries = ReadJournal.AsInstanceOf<IPersistenceIdsQuery>();

            Setup("k", 1);
            Setup("l", 1);
            Setup("m", 1);
            Setup("n", 1);
            Setup("o", 1);

            var source = queries.PersistenceIds();
            var probe = source.RunWith(this.SinkProbe<string>(), Materializer);

            probe.Within(TimeSpan.FromSeconds(10), () =>
            {
                probe.Request(2);
                probe.ExpectNext();
                probe.ExpectNext();
                probe.ExpectNoMsg(TimeSpan.FromMilliseconds(1000));

                probe.Request(2);
                probe.ExpectNext();
                probe.ExpectNext();
                probe.ExpectNoMsg(TimeSpan.FromMilliseconds(1000));

                return probe;
            });
        }

        [Fact]
        public virtual void ReadJournal_AllPersistenceIds_should_deliver_persistenceId_only_once_if_there_are_multiple_events()
        {
            var queries = ReadJournal.AsInstanceOf<IPersistenceIdsQuery>();

            Setup("p", 10);

            var source = queries.PersistenceIds();
            var probe = source.RunWith(this.SinkProbe<string>(), Materializer);

            probe.Within(TimeSpan.FromSeconds(10), () =>
            {
                return probe.Request(10)
                    .ExpectNext("p")
                    .ExpectNoMsg(TimeSpan.FromMilliseconds(200));
            });

            Setup("q", 10);

            probe.Within(TimeSpan.FromSeconds(10), () =>
            {
                return probe.Request(10)
                    .ExpectNext("q")
                    .ExpectNoMsg(TimeSpan.FromMilliseconds(200));
            });
        }

        [Fact]
        public virtual async Task ReadJournal_should_deallocate_AllPersistenceIds_publisher_when_the_last_subscriber_left()
        {
            if (AllocatesAllPersistenceIDsPublisher)
            {
                var journal = ReadJournal.AsInstanceOf<IPersistenceIdsQuery>();

                Setup("a", 1);
                Setup("b", 1);

                var source = journal.PersistenceIds();
                var probe =
                    source.RunWith(this.SinkProbe<string>(), Materializer);
                var probe2 =
                    source.RunWith(this.SinkProbe<string>(), Materializer);

                var fieldInfo = journal.GetType()
                    .GetField("_persistenceIdsPublisher",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.True(fieldInfo != null);

                // Assert that publisher is running.
                probe.Within(TimeSpan.FromSeconds(10), () => probe.Request(10)
                    .ExpectNextUnordered("a", "b")
                    .ExpectNoMsg(TimeSpan.FromMilliseconds(200)));

                probe.Cancel();

                // Assert that publisher is still alive when it still have a subscriber
                Assert.True(fieldInfo.GetValue(journal) is IPublisher<string>);

                probe2.Within(TimeSpan.FromSeconds(10), () => probe2.Request(4)
                    .ExpectNextUnordered("a", "b")
                    .ExpectNoMsg(TimeSpan.FromMilliseconds(200)));

                // Assert that publisher is de-allocated when the last subscriber left
                probe2.Cancel();
                await Task.Delay(400);
                Assert.True(fieldInfo.GetValue(journal) is null);
            }
        }

        protected IActorRef Setup(string persistenceId, int n)
        {
            var pref = Sys.ActorOf(Query.TestActor.Props(persistenceId));
            for (int i = 1; i <= n; i++)
            {
                pref.Tell($"{persistenceId}-{i}");
                ExpectMsg($"{persistenceId}-{i}-done");
            }

            return pref;
        }

        protected IActorRef WriteSnapshot(string persistenceId, int n)
        {
            var pref = Sys.ActorOf(Query.TestActor.Props(persistenceId));
            for (var i = 1; i <= n; i++)
            {
                pref.Tell($"{persistenceId}-{i}");
                ExpectMsg($"{persistenceId}-{i}-done");
            }

            var metadata = new SnapshotMetadata(persistenceId, n + 10);
            SnapshotStore.Tell(new SaveSnapshot(metadata, $"s-{n}"), _senderProbe.Ref);
            _senderProbe.ExpectMsg<SaveSnapshotSuccess>();

            return pref;
        }


        protected override void Dispose(bool disposing)
        {
            Materializer.Dispose();
            base.Dispose(disposing);
        }
    }
}
