//-----------------------------------------------------------------------
// <copyright file="SnapshotStoreProxy.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Persistence;
using Akka.Persistence.Snapshot;

namespace Akka.Cluster.Sharding.Tests
{
    public abstract class SnapshotStoreProxy : SnapshotStore, IWithUnboundedStash
    {
        private class InitTimeout
        {
            public static readonly InitTimeout Instance = new InitTimeout();

            private InitTimeout() { }
        }

        private bool _isInitialized;
        private bool _isInitTimedOut;
        private IActorRef _store;

        /// <summary>
        /// TBD
        /// </summary>
        protected SnapshotStoreProxy()
        {
            _isInitialized = false;
            _isInitTimedOut = false;
            _store = null;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public abstract TimeSpan Timeout { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public IStash Stash { get; set; }

        /// <summary>
        /// TBD
        /// </summary>
        public override void AroundPreStart()
        {
            Context.System.Scheduler.ScheduleTellOnce(Timeout, Self, InitTimeout.Instance, Self);
            base.AroundPreStart();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="receive">TBD</param>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected override bool AroundReceive(Receive receive, object message)
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

        protected async override Task DeleteAsync(SnapshotMetadata metadata)
        {
            if (_store == null)
                throw new TimeoutException("Store not intialized.");
            var s = Sender;
            try
            {
                var response = await _store.Ask(new DeleteSnapshot(metadata), Timeout);
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

        protected async override Task DeleteAsync(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            if (_store == null)
                throw new TimeoutException("Store not intialized.");
            var s = Sender;
            try
            {
                var response = await _store.Ask(new DeleteSnapshots(persistenceId, criteria), Timeout);
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

        protected override async Task<SelectedSnapshot> LoadAsync(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            if (_store == null)
                throw new TimeoutException("Store not intialized.");
            var s = Sender;
            try
            {
                var response = await _store.Ask(new LoadSnapshot(persistenceId, criteria, criteria.MaxSequenceNr), Timeout);
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

        protected override async Task SaveAsync(SnapshotMetadata metadata, object snapshot)
        {
            if (_store == null)
                throw new TimeoutException("Store not intialized.");
            var s = Sender;
            try
            {
                var response = await _store.Ask(new SaveSnapshot(metadata, snapshot), Timeout);
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
