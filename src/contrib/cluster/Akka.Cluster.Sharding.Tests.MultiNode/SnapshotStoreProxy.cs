//-----------------------------------------------------------------------
// <copyright file="SnapshotStoreProxy.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Persistence;
using Akka.Persistence.Snapshot;

namespace Akka.Cluster.Sharding.Tests
{
    public abstract class SnapshotStoreProxy : SnapshotStore, IWithUnboundedStash, IWithTimers
    {
        private const string TimeoutTimerKey = nameof(TimeoutTimerKey);
        
        private class InitTimeout
        {
            public static readonly InitTimeout Instance = new();

            private InitTimeout() { }
        }

        private bool _isInitialized;
        private bool _isInitTimedOut;
        private IActorRef _store;

        /// <summary>
        /// Initializes the snapshot-store proxy in an uninitialized state with no target store actor.
        /// </summary>
        protected SnapshotStoreProxy()
        {
            _isInitialized = false;
            _isInitTimedOut = false;
            _store = null;
        }

        /// <summary>
        /// The maximum wait before the proxy stops stashing messages for store initialization.
        /// </summary>
        public abstract TimeSpan Timeout { get; }

        /// <summary>
        /// The stash used while waiting for the store actor to be set.
        /// </summary>
        public IStash Stash { get; set; }

        public ITimerScheduler Timers { get; set; }

        /// <summary>
        /// Starts the initialization timeout before the snapshot store's base startup logic runs.
        /// </summary>
        public override void AroundPreStart()
        {
            Timers.StartSingleTimer(TimeoutTimerKey, InitTimeout.Instance, Timeout, Self);
            base.AroundPreStart();
        }

        /// <summary>
        /// Stashes messages until a store is supplied or initialization times out, then delegates normal snapshot-store handling.
        /// </summary>
        /// <param name="receive">The snapshot store's receive handler.</param>
        /// <param name="message">The incoming message to process or stash.</param>
        /// <returns><c>true</c> when initialization handling consumes the message; otherwise, the base snapshot-store result.</returns>
        protected internal override bool AroundReceive(Receive receive, object message)
        {
            if (_isInitialized)
            {
                if (!(message is InitTimeout))
                    return base.AroundReceive(receive, message);
            }
            else if (message is SetStore msg)
            {
                _store = msg.Store;
                Stash.UnstashAll();
                _isInitialized = true;
            }
            else if (message is InitTimeout)
            {
                _isInitTimedOut = true;
                Stash.UnstashAll(); // will trigger appropriate failures
            }
            else if (_isInitTimedOut)
            {
                return base.AroundReceive(receive, message);
            }
            else Stash.Stash();
            return true;
        }

        protected override async Task DeleteAsync(
            SnapshotMetadata metadata, 
            CancellationToken cancellationToken)
        {
            if (_store == null)
                throw new TimeoutException("Store not initialized.");
            try
            {
                var response = await _store.Ask(new DeleteSnapshot(metadata), Timeout, cancellationToken);
                if (response is DeleteSnapshotFailure f)
                {
                    ExceptionDispatchInfo.Capture(f.Cause).Throw();
                }
            }
            catch (AskTimeoutException)
            {
                throw new TimeoutException();
            }
        }

        protected override async Task DeleteAsync(
            string persistenceId, 
            SnapshotSelectionCriteria criteria, 
            CancellationToken cancellationToken)
        {
            if (_store == null)
                throw new TimeoutException("Store not initialized.");
            try
            {
                var response = await _store.Ask(new DeleteSnapshots(persistenceId, criteria), Timeout, cancellationToken);
                if (response is DeleteSnapshotsFailure f)
                {
                    ExceptionDispatchInfo.Capture(f.Cause).Throw();
                }
            }
            catch (AskTimeoutException)
            {
                throw new TimeoutException();
            }
        }

        protected override async Task<SelectedSnapshot> LoadAsync(
            string persistenceId,
            SnapshotSelectionCriteria criteria, 
            CancellationToken cancellationToken)
        {
            if (_store == null)
                throw new TimeoutException("Store not initialized.");
            try
            {
                var response = await _store.Ask(new LoadSnapshot(persistenceId, criteria, criteria.MaxSequenceNr), Timeout, cancellationToken);
                switch (response)
                {
                    case LoadSnapshotResult ls:
                        if (ls.Snapshot?.Snapshot != null)
                        {
                        }
                        return ls.Snapshot;
                    case LoadSnapshotFailed lf:
                        ExceptionDispatchInfo.Capture(lf.Cause).Throw();
                        break;
                }
            }
            catch (AskTimeoutException)
            {
                throw new TimeoutException();
            }
            throw new TimeoutException();
        }

        protected override async Task SaveAsync(
            SnapshotMetadata metadata,
            object snapshot, 
            CancellationToken cancellationToken)
        {
            if (_store == null)
                throw new TimeoutException("Store not initialized.");
            try
            {
                var response = await _store.Ask(new SaveSnapshot(metadata, snapshot), Timeout, cancellationToken);
                if (response is SaveSnapshotFailure f)
                {
                    ExceptionDispatchInfo.Capture(f.Cause).Throw();
                }
            }
            catch (AskTimeoutException)
            {
                throw new TimeoutException();
            }
        }
    }
}
