//-----------------------------------------------------------------------
// <copyright file="PersistenceTestKitBase.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Persistence.TestKit
{
    using System;
    using System.Threading.Tasks;
    using Actor;
    using Akka.TestKit;
    using Configuration;

    public abstract class PersistenceTestKitBase : TestKitBase
    {
        protected PersistenceTestKitBase(ITestKitAssertions assertions, string actorSystemName = null, string testActorName = null)
            : base(assertions, GetConfig(), actorSystemName, testActorName)
        {
            _persistenceExtension = Persistence.Instance.Apply(Sys);

            JournalActorRef = _persistenceExtension.JournalFor(null);
            Journal = TestJournal.FromRef(JournalActorRef);

            SnapshotsActorRef = _persistenceExtension.SnapshotStoreFor(null);
            Snapshots = TestSnapshotStore.FromRef(SnapshotsActorRef);
        }

        private readonly PersistenceExtension _persistenceExtension;

        public IActorRef JournalActorRef { get; }
        public IActorRef SnapshotsActorRef { get; }

        public ITestJournal Journal { get; } 
        public ITestSnapshotStore Snapshots { get; } 

        public async Task WithJournalRecovery(Func<JournalRecoveryBehavior, Task> behaviorSelector, Func<Task> execution)
        {
            try
            {
                await behaviorSelector(Journal.OnRecovery);
                await execution();
            }
            finally
            {
                await Journal.OnRecovery.Pass();
            }
        }

        public async Task WithJournalWrite(Func<JournalWriteBehavior, Task> behaviorSelector, Func<Task> execution)
        {
            try
            {
                await behaviorSelector(Journal.OnWrite);
                await execution();
            }
            finally
            {
                await Journal.OnWrite.Pass();
            }
        }

        public Task WithJournalRecovery(Func<JournalRecoveryBehavior, Task> behaviorSelector, Action execution)
            => WithJournalRecovery(behaviorSelector, () =>
            {
                execution();
                return Task.FromResult(new object());
            });

        public Task WithJournalWrite(Func<JournalWriteBehavior, Task> behaviorSelector, Action execution)
            => WithJournalWrite(behaviorSelector, () =>
            {
                execution();
                return Task.FromResult(new object());
            });

        public async Task WithSnapshotSave(Func<SnapshotStoreSaveBehavior, Task> behaviorSelector, Func<Task> execution)
        {
            try
            {
                await behaviorSelector(Snapshots.OnSave);
                await execution();
            }
            finally
            {
                await Snapshots.OnSave.Pass();
            }
        }

        public async Task WithSnapshotLoad(Func<SnapshotStoreLoadBehavior, Task> behaviorSelector, Func<Task> execution)
        {
            try
            {
                await behaviorSelector(Snapshots.OnLoad);
                await execution();
            }
            finally
            {
                await Snapshots.OnLoad.Pass();
            }
        }

        public async Task WithSnapshotDelete(Func<SnapshotStoreDeleteBehavior, Task> behaviorSelector, Func<Task> execution)
        {
            try
            {
                await behaviorSelector(Snapshots.OnDelete);
                await execution();
            }
            finally
            {
                await Snapshots.OnDelete.Pass();
            }
        }

        public Task WithSnapshotSave(Func<SnapshotStoreSaveBehavior, Task> behaviorSelector, Action execution)
            => WithSnapshotSave(behaviorSelector, () =>
            {
                execution();
                return Task.FromResult(true);
            });

        public Task WithSnapshotLoad(Func<SnapshotStoreLoadBehavior, Task> behaviorSelector, Action execution)
            => WithSnapshotLoad(behaviorSelector, () =>
            {
                execution();
                return Task.FromResult(true);
            });

        public Task WithSnapshotDelete(Func<SnapshotStoreDeleteBehavior, Task> behaviorSelector, Action execution)
            => WithSnapshotDelete(behaviorSelector, () =>
            {
                execution();
                return Task.FromResult(true);
            });
        
        static Config GetConfig()
            => ConfigurationFactory.FromResource<PersistenceTestKit>("Akka.Persistence.TestKit.test-journal.conf");
    }
}