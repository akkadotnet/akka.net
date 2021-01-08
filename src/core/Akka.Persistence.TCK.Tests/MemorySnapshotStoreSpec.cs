//-----------------------------------------------------------------------
// <copyright file="MemorySnapshotStoreSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Snapshot;
using Akka.Persistence.TCK.Snapshot;
using Akka.Util;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.TCK.Tests
{
    public class MemorySnapshotStoreSpec : SnapshotStoreSpec
    {
        public MemorySnapshotStoreSpec(ITestOutputHelper output) 
            : base(ConfigurationFactory.ParseString(
                @"akka.test.timefactor = 3
                  akka.persistence.snapshot-store.plugin = ""akka.persistence.snapshot-store.inmem"""), 
                  "MemorySnapshotStoreSpec",
                  output)
        {
            Initialize();
        }

        protected override bool SupportsSerialization => true;

        [Fact]
        public void MemorySnapshotStore_is_threadsafe()
        {
            EventFilter.Error().Expect(0, () =>
            {
                // get a few persistent actors going in parallel
                var sa1 = Sys.ActorOf(Props.Create(() => new SnapshotActor("sa1", TestActor)));
                var sa2 = Sys.ActorOf(Props.Create(() => new SnapshotActor("sa2", TestActor)));
                var sa3 = Sys.ActorOf(Props.Create(() => new SnapshotActor("sa3", TestActor)));

                Watch(sa1);
                Watch(sa2);
                Watch(sa3);

                var writeCount = 3000;

                var sas = new List<IActorRef>
                {
                    sa1,
                    sa2,
                    sa3
                };

                // hammer with write requests
                Parallel.ForEach(Enumerable.Range(0, writeCount), i =>
                {
                    sas[ThreadLocalRandom.Current.Next(0, 3)].Tell(i);
                });

                // spawn more persistence actors while writes are still going(?)
                var sa4 = Sys.ActorOf(Props.Create(() => new SnapshotActor("sa4", TestActor)));
                var sa5 = Sys.ActorOf(Props.Create(() => new SnapshotActor("sa5", TestActor)));
                var sa6 = Sys.ActorOf(Props.Create(() => new SnapshotActor("sa6", TestActor)));

                ReceiveN(writeCount).All(x => x is SaveSnapshotSuccess).Should().BeTrue("Expected all snapshot store saves to be successful, but some were not");

                // kill the existing snapshot stores, then re-create them to force recovery while the new snapshot actors
                // are still being written to.

                sa1.Tell(PoisonPill.Instance);
                ExpectTerminated(sa1);

                sa2.Tell(PoisonPill.Instance);
                ExpectTerminated(sa2);

                sa3.Tell(PoisonPill.Instance);
                ExpectTerminated(sa3);

                var sas2 = new List<IActorRef>
                {
                    sa4,
                    sa5,
                    sa6
                };

                // hammer with write requests
                Parallel.ForEach(Enumerable.Range(0, writeCount), i =>
                {
                    sas2[ThreadLocalRandom.Current.Next(0, 3)].Tell(i);
                });

                // recreate the previous entities
                var sa12 = Sys.ActorOf(Props.Create(() => new SnapshotActor("sa1", TestActor)));
                var sa22 = Sys.ActorOf(Props.Create(() => new SnapshotActor("sa2", TestActor)));
                var sa32 = Sys.ActorOf(Props.Create(() => new SnapshotActor("sa3", TestActor)));

                var sas12 = new List<IActorRef>
                {
                    sa12,
                    sa22,
                    sa32
                };

                // hammer other entities
                Parallel.ForEach(Enumerable.Range(0, writeCount), i =>
                {
                    sas12[ThreadLocalRandom.Current.Next(0, 3)].Tell(i);
                });

                ReceiveN(writeCount*2).All(x => x is SaveSnapshotSuccess).Should().BeTrue("Expected all snapshot store saves to be successful, but some were not");
            });
            
        }

        public class SnapshotActor : ReceivePersistentActor
        {
            private int _count = 0;
            private readonly IActorRef _reporter;

            public SnapshotActor(string persistenceId, IActorRef reporter)
            {
                PersistenceId = persistenceId;
                _reporter = reporter;

                Recover<SnapshotOffer>(offer =>
                {
                    if (offer.Snapshot is int i)
                    {
                        _count = i;
                    }
                });

                Command<int>(i =>
                {
                    _count += i;

                    Persist(i, i1 =>
                    {
                        SaveSnapshot(i);
                    });
                });

                Command<SaveSnapshotSuccess>(success => reporter.Tell(success));

                Command<SaveSnapshotFailure>(failure => reporter.Tell(failure));

                Command<string>(str => str.Equals("get"), s =>
                {
                    Sender.Tell(_count);
                });
            }

            public override string PersistenceId { get; }
        }
    }
}
