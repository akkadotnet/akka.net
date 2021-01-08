//-----------------------------------------------------------------------
// <copyright file="RootGuardianSupervisor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Dispatch.SysMsg;
using Akka.Event;
using Akka.Util;

namespace Akka.Actor
{
    /// <summary>
    /// Top-level anchor for the supervision hierarchy of this actor system.
    /// Note: This class is called theOneWhoWalksTheBubblesOfSpaceTime in Akka
    /// </summary>
    public class RootGuardianSupervisor : MinimalActorRef
    {
        private readonly ILoggingAdapter _log;
        private readonly TaskCompletionSource<Status> _terminationPromise;
        private readonly ActorPath _path;
        private readonly Switch _stopped = new Switch(false);
        private readonly IActorRefProvider _provider;

        private bool IsWalking => !_terminationPromise.Task.IsCompleted;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="root">TBD</param>
        /// <param name="provider">TBD</param>
        /// <param name="terminationPromise">TBD</param>
        /// <param name="log">TBD</param>
        public RootGuardianSupervisor(RootActorPath root, IActorRefProvider provider, TaskCompletionSource<Status> terminationPromise, ILoggingAdapter log)
        {
            _log = log;
            _terminationPromise = terminationPromise;
            _provider = provider;
            _path = root / "_Root-guardian-supervisor";   //In akka this is root / "bubble-walker" 
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <param name="sender">TBD</param>
        /// <exception cref="InvalidMessageException">This exception is thrown if the given <paramref name="message"/> is undefined.</exception>
        protected override void TellInternal(object message, IActorRef sender)
        {
            if (IsWalking)
            {
                if (message == null) throw new InvalidMessageException("Message is null");
                _log.Error("{0} received unexpected message [{1}]", _path, message);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="systemMessage">TBD</param>
        public override void SendSystemMessage(ISystemMessage systemMessage)
        {
            var failed = systemMessage as Failed;
            if (failed != null)
            {
                var cause = failed.Cause;
                var child = failed.Child;
                _log.Error(cause, "guardian {0} failed, shutting down!", child);
                CauseOfTermination = cause;
                ((IInternalActorRef)child).Stop();
                return;
            }
            var supervise = systemMessage as Supervise;
            if (supervise != null)
            {
                // This comment comes from AKKA: TO DO register child in some map to keep track of it and enable shutdown after all dead
                return;
            }
            var deathWatchNotification = systemMessage as DeathWatchNotification;
            if (deathWatchNotification != null)
            {
                Stop();
                return;
            }
            _log.Error("{0} received unexpected system message [{1}]", _path, systemMessage);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public Exception CauseOfTermination { get; private set; }
        /// <summary>
        /// TBD
        /// </summary>
        public override void Stop()
        {
            var causeOfTermination = CauseOfTermination;
            var status = causeOfTermination == null ? (Status)new Status.Success(null) : new Status.Failure(causeOfTermination);
            _terminationPromise.SetResult(status);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override ActorPath Path
        {
            get { return _path; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override IActorRefProvider Provider
        {
            get { return _provider; }
        }
    }
}

