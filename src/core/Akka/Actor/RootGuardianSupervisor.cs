//-----------------------------------------------------------------------
// <copyright file="RootGuardianSupervisor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        private readonly Switch _stopped = new(false);
        private readonly IActorRefProvider _provider;

        private bool IsWalking => !_terminationPromise.Task.IsCompleted;

        /// <summary>
        /// Creates the supervisor responsible for completing actor system termination.
        /// </summary>
        /// <param name="root">The actor system's root path.</param>
        /// <param name="provider">The actor reference provider for the system.</param>
        /// <param name="terminationPromise">The task completed with the system's final status.</param>
        /// <param name="log">The logger used for unexpected messages and guardian failures.</param>
        public RootGuardianSupervisor(RootActorPath root, IActorRefProvider provider, TaskCompletionSource<Status> terminationPromise, ILoggingAdapter log)
        {
            _log = log;
            _terminationPromise = terminationPromise;
            _provider = provider;
            _path = root / "_Root-guardian-supervisor";   //In akka this is root / "bubble-walker" 
        }

        /// <summary>
        /// Logs ordinary messages received while the root supervisor is waiting for termination.
        /// </summary>
        /// <param name="message">The message received.</param>
        /// <param name="sender">The sender of the message.</param>
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
        /// Handles root guardian failures and termination notifications.
        /// </summary>
        /// <param name="systemMessage">The system message to process.</param>
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
        /// The cause recorded from a failed root guardian, if termination followed a failure.
        /// </summary>
        public Exception CauseOfTermination { get; private set; }
        /// <summary>
        /// Completes the system termination task with success or the recorded guardian failure.
        /// </summary>
        public override void Stop()
        {
            var causeOfTermination = CauseOfTermination;
            var status = causeOfTermination == null ? (Status)new Status.Success(null) : new Status.Failure(causeOfTermination);
            _terminationPromise.SetResult(status);
        }

        /// <summary>
        /// The path assigned to this root supervisor.
        /// </summary>
        public override ActorPath Path
        {
            get { return _path; }
        }

        /// <summary>
        /// The actor reference provider for this supervisor.
        /// </summary>
        public override IActorRefProvider Provider
        {
            get { return _provider; }
        }
    }
}
