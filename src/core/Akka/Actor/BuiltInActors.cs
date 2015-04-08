//-----------------------------------------------------------------------
// <copyright file="BuiltInActors.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Dispatch.SysMsg;
using Akka.Event;

namespace Akka.Actor
{
    /// <summary>
    ///     Class EventStreamActor.
    /// </summary>
    public class EventStreamActor : ActorBase
    {
        /// <summary>
        ///     Processor for user defined messages.
        /// </summary>
        /// <param name="message">The message.</param>
        protected override bool Receive(object message)
        {
            return true;
        }
    }

    /// <summary>
    ///     Class GuardianActor.
    /// </summary>
    public class GuardianActor : ActorBase
    {
        protected override bool Receive(object message)
        {
            if(message is Terminated)
                Context.Stop(Self);
            else if(message is StopChild)
                Context.Stop(((StopChild)message).Child);
            else
                Context.System.DeadLetters.Tell(new DeadLetter(message, Sender, Self), Sender);
            return true;
        }
    }

    public class SystemGuardianActor : ActorBase
    {
        private readonly IActorRef _userGuardian;
        private readonly HashSet<IActorRef> _terminationHooks;

        public SystemGuardianActor(IActorRef userGuardian)
        {
            _userGuardian = userGuardian;
            _terminationHooks = new HashSet<IActorRef>();
        }

        /// <summary>
        /// Processor for messages that are sent to the root system guardian
        /// </summary>
        /// <param name="message"></param>
        protected override bool Receive(object message)
        {
            var terminated = message as Terminated;
            if(terminated != null)
            {
                var terminatedActor = terminated.ActorRef;
                if(_userGuardian.Equals(terminatedActor))
                {
                    // time for the systemGuardian to stop, but first notify all the
                    // termination hooks, they will reply with TerminationHookDone
                    // and when all are done the systemGuardian is stopped
                    Context.Become(Terminating);
                    foreach(var terminationHook in _terminationHooks)
                    {
                        terminationHook.Tell(TerminationHook.Instance);
                    }
                    StopWhenAllTerminationHooksDone();
                }
                else
                {
                    // a registered, and watched termination hook terminated before
                    // termination process of guardian has started
                    _terminationHooks.Remove(terminatedActor);
                }
                return true;
            }
            
            var stopChild = message as StopChild;
            if(stopChild != null)
            {
                Context.Stop(stopChild.Child);
                return true;
            }
            var sender = Sender;
            
            var registerTerminationHook = message as RegisterTerminationHook;
            if(registerTerminationHook != null && !ReferenceEquals(sender, Context.System.DeadLetters))
            {
                _terminationHooks.Add(sender);
                Context.Watch(sender);
                return true;
            }
            Context.System.DeadLetters.Tell(new DeadLetter(message, sender, Self), sender);
            return true;
        }

        private bool Terminating(object message)
        {
            var terminated = message as Terminated;
            if(terminated != null)
            {
                StopWhenAllTerminationHooksDone(terminated.ActorRef);
                return true;
            }
            var sender = Sender;

            var terminationHookDone = message as TerminationHookDone;
            if(terminationHookDone != null)
            {
                StopWhenAllTerminationHooksDone(sender);
                return true;
            }
            Context.System.DeadLetters.Tell(new DeadLetter(message, sender, Self), sender);
            return true;
        }

        private void StopWhenAllTerminationHooksDone(IActorRef terminatedActor)
        {
            _terminationHooks.Remove(terminatedActor);
            StopWhenAllTerminationHooksDone();
        }

        private void StopWhenAllTerminationHooksDone()
        {
            if(_terminationHooks.Count == 0)
            {
                var actorSystem = Context.System;
                actorSystem.EventStream.StopDefaultLoggers(actorSystem);
                Context.Stop(Self);
            }
        }

        protected override void PreRestart(Exception reason, object message)
        {
            //Guardian MUST NOT lose its children during restart
            //Intentionally left blank
        }
    }

    /// <summary>
    ///     Class DeadLetterActorRef.
    /// </summary>
    public class DeadLetterActorRef : EmptyLocalActorRef
    {
        private readonly EventStream _eventStream;

        public DeadLetterActorRef(IActorRefProvider provider, ActorPath path, EventStream eventStream)
            : base(provider, path, eventStream)
        {
            _eventStream = eventStream;
        }

        //TODO: Since this isn't overriding SendUserMessage it doesn't handle all messages as Akka JVM does

        protected override void HandleDeadLetter(DeadLetter deadLetter)
        {
            if(!SpecialHandle(deadLetter.Message, deadLetter.Sender))
                _eventStream.Publish(deadLetter);
        }

        protected override bool SpecialHandle(object message, IActorRef sender)
        {
            var w = message as Watch;
            if(w != null)
            {
                if(w.Watchee != this && w.Watcher != this)
                {
                    w.Watcher.Tell(new DeathWatchNotification(w.Watchee, existenceConfirmed: false, addressTerminated: false));
                }
                return true;
            }
            return base.SpecialHandle(message, sender);
        }
    }
}

