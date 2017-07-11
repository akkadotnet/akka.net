//-----------------------------------------------------------------------
// <copyright file="BuiltInActors.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Dispatch;
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
        /// <returns>TBD</returns>
        protected override bool Receive(object message)
        {
            return true;
        }
    }

    /// <summary>
    ///     Class GuardianActor.
    /// </summary>
    public class GuardianActor : ActorBase, IRequiresMessageQueue<IUnboundedMessageQueueSemantics>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected override bool Receive(object message)
        {
            switch (message)
            {
                case Terminated _:
                    Context.Stop(Self);
                    return true;
                case StopChild stopChild:
                    Context.Stop(stopChild.Child);
                    return true;
                default:
                    Context.System.DeadLetters.Tell(new DeadLetter(message, Sender, Self), Sender);
                    return true;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PreStart()
        {
            // guardian MUST NOT lose its children during restart
        }
    }

    /// <summary>
    /// System guardian. 
    /// 
    /// Root actor for all actors under the /system path.
    /// </summary>
    public class SystemGuardianActor : ActorBase, IRequiresMessageQueue<IUnboundedMessageQueueSemantics>
    {
        private readonly IActorRef _userGuardian;
        private readonly HashSet<IActorRef> _terminationHooks;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="userGuardian">TBD</param>
        public SystemGuardianActor(IActorRef userGuardian)
        {
            _userGuardian = userGuardian;
            _terminationHooks = new HashSet<IActorRef>();
        }

        /// <summary>
        /// Processor for messages that are sent to the root system guardian
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected override bool Receive(object message)
        {
            var sender = Sender;

            switch (message)
            {
                case Terminated terminated:
                    var terminatedActor = terminated.ActorRef;
                    if (_userGuardian.Equals(terminatedActor))
                    {
                        // time for the systemGuardian to stop, but first notify all the
                        // termination hooks, they will reply with TerminationHookDone
                        // and when all are done the systemGuardian is stopped
                        Context.Become(Terminating);
                        foreach (var terminationHook in _terminationHooks)
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
                case StopChild stopChild:
                    Context.Stop(stopChild.Child);
                    return true;
                case RegisterTerminationHook _:
                    if (!ReferenceEquals(sender, Context.System.DeadLetters))
                    {
                        _terminationHooks.Add(sender);
                        Context.Watch(sender);
                    }

                    return true;
                default:
                    Context.System.DeadLetters.Tell(new DeadLetter(message, sender, Self), sender);
                    return true;
            }
        }

        private bool Terminating(object message)
        {
            var sender = Sender;

            switch (message)
            {
                case Terminated terminated:
                    StopWhenAllTerminationHooksDone(terminated.ActorRef);
                    return true;
                case TerminationHookDone _:
                    StopWhenAllTerminationHooksDone(sender);
                    return true;
                default:
                    Context.System.DeadLetters.Tell(new DeadLetter(message, sender, Self), sender);
                    return true;
            }
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="reason">TBD</param>
        /// <param name="message">TBD</param>
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="provider">TBD</param>
        /// <param name="path">TBD</param>
        /// <param name="eventStream">TBD</param>
        public DeadLetterActorRef(IActorRefProvider provider, ActorPath path, EventStream eventStream)
            : base(provider, path, eventStream)
        {
            _eventStream = eventStream;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <param name="sender">TBD</param>
        /// <exception cref="InvalidMessageException">This exception is thrown if the given <paramref name="message"/> is undefined.</exception>
        protected override void TellInternal(object message, IActorRef sender)
        {
            if (message == null)
                throw new InvalidMessageException("Message is null");

            if (message is Identify identify)
            {
                sender.Tell(new ActorIdentity(identify.MessageId, ActorRefs.Nobody));
                return;
            }
            if(message is DeadLetter deadLetter)
            {
                if (!SpecialHandle(deadLetter.Message, deadLetter.Sender))
                {
                    _eventStream.Publish(deadLetter);
                }
                return;
            }
            if (!SpecialHandle(message, sender))
            {
                _eventStream.Publish(new DeadLetter(message, sender.IsNobody() ? Provider.DeadLetters : sender, this));
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <param name="sender">TBD</param>
        /// <returns>TBD</returns>
        protected override bool SpecialHandle(object message, IActorRef sender)
        {
            if (message is Watch watch)
            {
                if (!watch.Watchee.Equals(this) && !watch.Watcher.Equals(this))
                {
                    watch.Watcher.SendSystemMessage(new DeathWatchNotification(watch.Watchee, false, false));
                }

                return true;
            }

            return base.SpecialHandle(message, sender);
        }
    }
}

