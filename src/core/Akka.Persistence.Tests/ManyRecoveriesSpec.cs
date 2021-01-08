//-----------------------------------------------------------------------
// <copyright file="ManyRecoveriesSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;

namespace Akka.Persistence.Tests
{
    public class ManyRecoveriesSpec : PersistenceSpec
    {
        public class TestPersistentActor : UntypedPersistentActor
        {
            public override string PersistenceId { get; }
            public TestLatch Latch { get; }

            public static Props Props(string name, TestLatch latch) =>
                Actor.Props.Create(() => new TestPersistentActor(name, latch));

            public TestPersistentActor(string name, TestLatch latch)
            {
                PersistenceId = name;
                Latch = latch;
            }

            protected override void OnRecover(object message)
            {
                if (message is Evt)
                {
                    Latch?.Ready(TimeSpan.FromSeconds(10));
                }
            }

            protected override void OnCommand(object message)
            {
                if (message is Cmd cmd)
                {
                    Persist(new Evt(cmd.Data), _ =>
                    {
                        Sender.Tell($"{PersistenceId}-{cmd.Data}-{LastSequenceNr}", Self);
                    });
                }
                else if (message is "stop")
                {
                    Context.Stop(Self);
                }
            }
        }

        public ManyRecoveriesSpec() : base(ConfigurationFactory.ParseString(@"
            akka.actor.default-dispatcher.Type = ForkJoinDispatcher
            akka.actor.default-dispatcher.dedicated-thread-pool.thread-count = 5
            akka.persistence.max-concurrent-recoveries = 3
            akka.persistence.journal.plugin = ""akka.persistence.journal.inmem""

            # snapshot store plugin is NOT defined, things should still work
            akka.persistence.snapshot-store.plugin = ""akka.persistence.no-snapshot-store""
            akka.persistence.snapshot-store.local.dir = ""target/snapshots-" + typeof(ManyRecoveriesSpec).FullName + @""""))
        {
        }

        [Fact]
        public void Many_persistent_actors_must_be_able_to_recover_without_overloading()
        {
            Enumerable.Range(1, 100).ForEach(n =>
            {
                Sys.ActorOf(TestPersistentActor.Props($"a{n}", null)).Tell(new Cmd("A"));
                ExpectMsg($"a{n}-A-1");
            });

            // This would starve (block) all threads without max-concurrent-recoveries
            var latch = new TestLatch();
            Enumerable.Range(1, 100).ForEach(n =>
            {
                Sys.ActorOf(TestPersistentActor.Props($"a{n}", latch)).Tell(new Cmd("B"));
            });

            // This should be able to progress even though above is blocking, 2 remaining non-blocked threads
            Enumerable.Range(1, 10).ForEach(n =>
            {
                Sys.ActorOf(EchoActor.Props(this)).Tell(n);
                ExpectMsg(n);
            });

            latch.CountDown();
            ReceiveN(100).ShouldAllBeEquivalentTo(Enumerable.Range(1, 100).Select(n => $"a{n}-B-2"));
        }
    }
}
