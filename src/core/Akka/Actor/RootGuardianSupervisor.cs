//-----------------------------------------------------------------------
// <copyright file="RootGuardianSupervisor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
        private readonly Switch _stopped=new Switch(false);
        private readonly IActorRefProvider _provider;

        public RootGuardianSupervisor(RootActorPath root, IActorRefProvider provider, TaskCompletionSource<Status> terminationPromise, ILoggingAdapter log)
        {
            _log = log;
            _terminationPromise = terminationPromise;
            _provider = provider;
            _path = root / "_Root-guardian-supervisor";   //In akka this is root / "bubble-walker" 
        }

        protected override void TellInternal(object message, IActorRef sender)
        {
            var systemMessage = message as ISystemMessage;
            if(systemMessage!=null)
            {
                SendSystemMessage(systemMessage);
            }
            else
            {
                _stopped.IfOff(() =>
                {
                    if(message == null) throw new InvalidMessageException();
                    _log.Error("{0} received unexpected message [{1}]", _path, message);
                });
            }
        }

        private void SendSystemMessage(ISystemMessage systemMessage)
        {
            _stopped.IfOff(() =>
            {
                var failed = systemMessage as Failed;
                if(failed != null)
                {
                    var cause = failed.Cause;
                    var child = failed.Child;
                    _log.Error(cause, "guardian {0} failed, shutting down!", child);
                    CauseOfTermination = cause;
                    ((IInternalActorRef) child).Stop();
                    return;
                }
                var supervise = systemMessage as Supervise;
                if(supervise != null)
                {
                    // This comment comes from AKKA: TO DO register child in some map to keep track of it and enable shutdown after all dead
                    return;
                }
                var deathWatchNotification = systemMessage as DeathWatchNotification;
                if(deathWatchNotification != null)
                {
                    Stop();
                    return;
                }
                _log.Error("{0} received unexpected system message [{1}]",_path,systemMessage);
            });
        }

        public Exception CauseOfTermination { get; private set; }
        public override void Stop()
        {
            _stopped.SwitchOn(() =>
            {
                var causeOfTermination = CauseOfTermination;
                var status = causeOfTermination == null ? (Status) new Status.Success(null) : new Status.Failure(causeOfTermination);
                _terminationPromise.SetResult(status);
            });
        }
       
        public override ActorPath Path
        {
            get { return _path; }
        }

        public override IActorRefProvider Provider
        {
            get { return _provider; }
        }
    }
}

