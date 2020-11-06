//-----------------------------------------------------------------------
// <copyright file="PersistenceTestKit.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor.Setup;
using Akka.Configuration;

namespace Akka.Persistence.TestKit
{
    using System;
    using System.Threading.Tasks;
    using Actor;
    using Akka.TestKit.Xunit2;
    using Configuration;
    using Xunit.Abstractions;

    /// <summary>
    /// This class represents an Akka.NET Persistence TestKit that uses <a href="https://xunit.github.io/">xUnit</a>
    /// as its testing framework.
    /// </summary>
    public abstract class PersistenceTestKit : TestKit
    {
        /// <summary>
        /// Create a new instance of the <see cref="PersistenceTestKit"/> class.
        /// A new system with the specified configuration will be created.
        /// </summary>
        /// <param name="setup">Test ActorSystem configuration</param>
        /// <param name="actorSystemName">Optional: The name of the actor system</param>
        /// <param name="output">TBD</param>
        protected PersistenceTestKit(ActorSystemSetup setup, string actorSystemName = null, ITestOutputHelper output = null)
            : base(GetConfig(setup), actorSystemName, output)
        {
            var persistenceExtension = Persistence.Instance.Apply(Sys);

            JournalActorRef = persistenceExtension.JournalFor(null);
            Journal = TestJournal.FromRef(JournalActorRef);

            SnapshotsActorRef = persistenceExtension.SnapshotStoreFor(null);
            Snapshots = TestSnapshotStore.FromRef(SnapshotsActorRef);
        }

        /// <summary>
        /// Create a new instance of the <see cref="PersistenceTestKit"/> class.
        /// A new system with the specified configuration will be created.
        /// </summary>
        /// <param name="config">Test ActorSystem configuration</param>
        /// <param name="actorSystemName">Optional: The name of the actor system</param>
        /// <param name="output">TBD</param>
        protected PersistenceTestKit(Config config, string actorSystemName = null, ITestOutputHelper output = null)
            : base(GetConfig(config), actorSystemName, output)
        {
            var persistenceExtension = Persistence.Instance.Apply(Sys);

            JournalActorRef = persistenceExtension.JournalFor(null);
            Journal = TestJournal.FromRef(JournalActorRef);

            SnapshotsActorRef = persistenceExtension.SnapshotStoreFor(null);
            Snapshots = TestSnapshotStore.FromRef(SnapshotsActorRef);
        }

        /// <summary>
        /// Create a new instance of the <see cref="PersistenceTestKit"/> class.
        /// A new system with the default configuration will be created.
        /// </summary>
        /// <param name="actorSystemName">Optional: The name of the actor system</param>
        /// <param name="output">TBD</param>
        protected PersistenceTestKit(string actorSystemName = null, ITestOutputHelper output = null)
            : this(Config.Empty, actorSystemName, output)
        {
        }

        /// <summary>
        /// Actor reference to persistence Journal used by current actor system.
        /// </summary>
        public IActorRef JournalActorRef { get; }

        /// <summary>
        /// Actor reference to persistence Snapshot Store used by current actor system.
        /// </summary>
        public IActorRef SnapshotsActorRef { get; }

        /// <summary>
        /// 
        /// </summary>
        public ITestJournal Journal { get; } 

        /// <summary>
        /// 
        /// </summary>
        public ITestSnapshotStore Snapshots { get; } 

        /// <summary>
        ///     Execute <paramref name="execution"/> delegate with Journal Behavior applied to Recovery operation.
        /// </summary>
        /// <remarks>
        ///     After <paramref name="execution"/> will be executed, Recovery behavior will be reverted back to normal.
        /// </remarks>
        /// <param name="behaviorSelector">Delegate which will select Journal behavior.</param>
        /// <param name="execution">Async delegate which will be executed with applied Journal behavior.</param>
        /// <returns><see cref="Task"/> which must be awaited.</returns>
        public async Task WithJournalRecovery(Func<JournalRecoveryBehavior, Task> behaviorSelector, Func<Task> execution)
        {
            if (behaviorSelector == null) throw new ArgumentNullException(nameof(behaviorSelector));
            if (execution == null) throw new ArgumentNullException(nameof(execution));

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

        /// <summary>
        ///     Execute <paramref name="execution"/> delegate with Journal Behavior applied to Write operation.
        /// </summary>
        /// <remarks>
        ///     After <paramref name="execution"/> will be executed, Write behavior will be reverted back to normal.
        /// </remarks>
        /// <param name="behaviorSelector">Delegate which will select Journal behavior.</param>
        /// <param name="execution">Async delegate which will be executed with applied Journal behavior.</param>
        /// <returns><see cref="Task"/> which must be awaited.</returns>
        public async Task WithJournalWrite(Func<JournalWriteBehavior, Task> behaviorSelector, Func<Task> execution)
        {
            if (behaviorSelector == null) throw new ArgumentNullException(nameof(behaviorSelector));
            if (execution == null) throw new ArgumentNullException(nameof(execution));

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

        /// <summary>
        ///     Execute <paramref name="execution"/> delegate with Journal Behavior applied to Recovery operation.
        /// </summary>
        /// <remarks>
        ///     After <paramref name="execution"/> will be executed, Recovery behavior will be reverted back to normal.
        /// </remarks>
        /// <param name="behaviorSelector">Delegate which will select Journal behavior.</param>
        /// <param name="execution">Delegate which will be executed with applied Journal behavior.</param>
        /// <returns><see cref="Task"/> which must be awaited.</returns>
        public Task WithJournalRecovery(Func<JournalRecoveryBehavior, Task> behaviorSelector, Action execution)
            => WithJournalRecovery(behaviorSelector, () =>
            {
                if (execution == null) throw new ArgumentNullException(nameof(execution));

                execution();
                return Task.FromResult(new object());
            });

        /// <summary>
        ///     Execute <paramref name="execution"/> delegate with Journal Behavior applied to Write operation.
        /// </summary>
        /// <remarks>
        ///     After <paramref name="execution"/> will be executed, Write behavior will be reverted back to normal.
        /// </remarks>
        /// <param name="behaviorSelector">Delegate which will select Journal behavior.</param>
        /// <param name="execution">Delegate which will be executed with applied Journal behavior.</param>
        /// <returns><see cref="Task"/> which must be awaited.</returns>
        public Task WithJournalWrite(Func<JournalWriteBehavior, Task> behaviorSelector, Action execution)
            => WithJournalWrite(behaviorSelector, () =>
            {
                if (execution == null) throw new ArgumentNullException(nameof(execution));

                execution();
                return Task.FromResult(new object());
            });

        /// <summary>
        ///     Execute <paramref name="execution"/> delegate with Snapshot Store Behavior applied to Save operation.
        /// </summary>
        /// <remarks>
        ///     After <paramref name="execution"/> will be executed, Save behavior will be reverted back to normal.
        /// </remarks>
        /// <param name="behaviorSelector">Delegate which will select Snapshot Store behavior.</param>
        /// <param name="execution">Async delegate which will be executed with applied Journal behavior.</param>
        /// <returns><see cref="Task"/> which must be awaited.</returns>
        public async Task WithSnapshotSave(Func<SnapshotStoreSaveBehavior, Task> behaviorSelector, Func<Task> execution)
        {
            if (behaviorSelector == null) throw new ArgumentNullException(nameof(behaviorSelector));
            if (execution == null) throw new ArgumentNullException(nameof(execution));

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

        /// <summary>
        ///     Execute <paramref name="execution"/> delegate with Snapshot Store Behavior applied to Load operation.
        /// </summary>
        /// <remarks>
        ///     After <paramref name="execution"/> will be executed, Load behavior will be reverted back to normal.
        /// </remarks>
        /// <param name="behaviorSelector">Delegate which will select Snapshot Store behavior.</param>
        /// <param name="execution">Async delegate which will be executed with applied Journal behavior.</param>
        /// <returns><see cref="Task"/> which must be awaited.</returns>
        public async Task WithSnapshotLoad(Func<SnapshotStoreLoadBehavior, Task> behaviorSelector, Func<Task> execution)
        {
            if (behaviorSelector == null) throw new ArgumentNullException(nameof(behaviorSelector));
            if (execution == null) throw new ArgumentNullException(nameof(execution));

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

        /// <summary>
        ///     Execute <paramref name="execution"/> delegate with Snapshot Store Behavior applied to Delete operation.
        /// </summary>
        /// <remarks>
        ///     After <paramref name="execution"/> will be executed, Delete behavior will be reverted back to normal.
        /// </remarks>
        /// <param name="behaviorSelector">Delegate which will select Snapshot Store behavior.</param>
        /// <param name="execution">Async delegate which will be executed with applied Journal behavior.</param>
        /// <returns><see cref="Task"/> which must be awaited.</returns>
        public async Task WithSnapshotDelete(Func<SnapshotStoreDeleteBehavior, Task> behaviorSelector, Func<Task> execution)
        {
            if (behaviorSelector == null) throw new ArgumentNullException(nameof(behaviorSelector));
            if (execution == null) throw new ArgumentNullException(nameof(execution));

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

        /// <summary>
        ///     Execute <paramref name="execution"/> delegate with Snapshot Store Behavior applied to Save operation.
        /// </summary>
        /// <remarks>
        ///     After <paramref name="execution"/> will be executed, Save behavior will be reverted back to normal.
        /// </remarks>
        /// <param name="behaviorSelector">Delegate which will select Snapshot Store behavior.</param>
        /// <param name="execution">Delegate which will be executed with applied Journal behavior.</param>
        /// <returns><see cref="Task"/> which must be awaited.</returns>
        public Task WithSnapshotSave(Func<SnapshotStoreSaveBehavior, Task> behaviorSelector, Action execution)
            => WithSnapshotSave(behaviorSelector, () =>
            {
                if (execution == null) throw new ArgumentNullException(nameof(execution));

                execution();
                return Task.FromResult(true);
            });

        /// <summary>
        ///     Execute <paramref name="execution"/> delegate with Snapshot Store Behavior applied to Load operation.
        /// </summary>
        /// <remarks>
        ///     After <paramref name="execution"/> will be executed, Load behavior will be reverted back to normal.
        /// </remarks>
        /// <param name="behaviorSelector">Delegate which will select Snapshot Store behavior.</param>
        /// <param name="execution">Async delegate which will be executed with applied Journal behavior.</param>
        /// <returns><see cref="Task"/> which must be awaited.</returns>
        public Task WithSnapshotLoad(Func<SnapshotStoreLoadBehavior, Task> behaviorSelector, Action execution)
            => WithSnapshotLoad(behaviorSelector, () =>
            {
                if (execution == null) throw new ArgumentNullException(nameof(execution));

                execution();
                return Task.FromResult(true);
            });

        /// <summary>
        ///     Execute <paramref name="execution"/> delegate with Snapshot Store Behavior applied to Delete operation.
        /// </summary>
        /// <remarks>
        ///     After <paramref name="execution"/> will be executed, Delete behavior will be reverted back to normal.
        /// </remarks>
        /// <param name="behaviorSelector">Delegate which will select Snapshot Store behavior.</param>
        /// <param name="execution">Async delegate which will be executed with applied Journal behavior.</param>
        /// <returns><see cref="Task"/> which must be awaited.</returns>
        public Task WithSnapshotDelete(Func<SnapshotStoreDeleteBehavior, Task> behaviorSelector, Action execution)
            => WithSnapshotDelete(behaviorSelector, () =>
            {
                if (execution == null) throw new ArgumentNullException(nameof(execution));

                execution();
                return Task.FromResult(true);
            });

        /// <summary>
        ///     Loads from embedded resources actor system persistence configuration with <see cref="TestJournal"/> and
        ///     <see cref="TestSnapshotStore"/> configured as default persistence plugins.
        /// </summary>
        /// <param name="customConfig">Custom configuration that was passed in the constructor.</param>
        /// <returns>Actor system configuration object.</returns>
        /// <seealso cref="Config"/>
        private static ActorSystemSetup GetConfig(ActorSystemSetup customConfig)
        {
            var bootstrapSetup = customConfig.Get<BootstrapSetup>();
            var config = bootstrapSetup.FlatSelect(x => x.Config);
            var actorProvider = bootstrapSetup.FlatSelect(x => x.ActorRefProvider);
            var newSetup = BootstrapSetup.Create();
            if (config.HasValue)
            {
                newSetup = newSetup.WithConfig(GetConfig(config.Value));
            }
            else
            {
                newSetup = newSetup.WithConfig(GetConfig(Config.Empty));
            }

            if (actorProvider.HasValue)
            {
                newSetup = newSetup.WithActorRefProvider(actorProvider.Value);
            }

            return customConfig.WithSetup(newSetup);
        }

        /// <summary>
        ///     Loads from embedded resources actor system persistence configuration with <see cref="TestJournal"/> and
        ///     <see cref="TestSnapshotStore"/> configured as default persistence plugins.
        /// </summary>
        /// <param name="customConfig">Custom configuration that was passed in the constructor.</param>
        /// <returns>Actor system configuration object.</returns>
        /// <seealso cref="Config"/>
        private static Config GetConfig(Config customConfig)
        {
            var defaultConfig = ConfigurationFactory.FromResource<TestJournal>("Akka.Persistence.TestKit.config.conf");
            if (customConfig == Config.Empty) return defaultConfig;
            else return defaultConfig.SafeWithFallback(customConfig);
        }        
    }
}
