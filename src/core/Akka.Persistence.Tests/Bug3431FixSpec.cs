using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Snapshot;
using Akka.TestKit;
using Xunit;
using FluentAssertions;
using Xunit.Abstractions;

namespace Akka.Persistence.Tests
{
    /// <summary>
    /// Specs to validate the whether or not https://github.com/akkadotnet/akka.net/issues/3431 is an issue
    /// </summary>
    public class Bug3431FixSpec : AkkaSpec
    {
        public Bug3431FixSpec(ITestOutputHelper helper) : base(helper) { }

        /// <summary>
        /// Aggressively saves snapshots to the <see cref="LocalSnapshotStore"/> without 
        /// incrementing any sequence numbers. Tries on purpose to create two conflicting snapshots
        /// with the same ID at the same time.
        /// </summary>
        public class AggressiveSnapshotStoreActor : ReceivePersistentActor
        {
            private readonly IActorRef _testActor;
            private int _currentCount = 0; // this is our snapshot value
            public override string PersistenceId => Context.Self.Path.Name;

            public AggressiveSnapshotStoreActor(IActorRef testActor)
            {
                _testActor = testActor;

                Command<SaveSnapshotSuccess>(_ => _testActor.Tell(_));

                Command<SaveSnapshotFailure>(_ => _testActor.Tell(_));

                CommandAny(_ =>
                {
                    _currentCount++;
                    SaveSnapshot(_currentCount);
                });

                Recover<SnapshotOffer>(offer =>
                {
                    
                });
            }

            protected override void PostStop()
            {
                DeleteSnapshot(0); // delete our snapshot
            }
        }

        [Fact]
        public void Should_save_concurrent_Snapshots_to_LocalSnapshotStore()
        {
            var snapshotCount = 25;
            var snapshotActor = Sys.ActorOf(Props.Create(() => new AggressiveSnapshotStoreActor(TestActor)));

            for (var i = 0; i < snapshotCount; i++)
            {
                snapshotActor.Tell(i);
            }

            var msgs = ReceiveN(snapshotCount, TimeSpan.FromSeconds(10));
            var allSaved = msgs.All(x => x is SaveSnapshotSuccess);
            if (!allSaved)
            {
                var exceptions = msgs.Where(x => !(x is SaveSnapshotSuccess)).ToList();
                var errorMsg = "";
                foreach (var ex in exceptions)
                {
                    errorMsg += ex.ToString();
                }
                false.Should().BeTrue("expected all snapshots to be saved, but found {0} that were not. Output: {1}",
                    exceptions.Count, errorMsg);
            }
        }
    }
}
