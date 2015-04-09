//-----------------------------------------------------------------------
// <copyright file="InternalTestActorRef.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Actor.Internals;
using Akka.Dispatch;
using Akka.Pattern;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.TestKit.Internal
{
    /// <summary>INTERNAL
    /// This special ActorRef is exclusively for use during unit testing in a single-threaded environment. Therefore, it
    /// overrides the dispatcher to CallingThreadDispatcher and sets the receiveTimeout to None. Otherwise,
    /// it acts just like a normal ActorRef. You may retrieve a reference to the underlying actor to test internal logic.
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    public class InternalTestActorRef : LocalActorRef
    {
        private static readonly AtomicCounterLong _uniqueNameNumber = new AtomicCounterLong(0);

        /// <summary>INTERNAL
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        /// </summary>
        public static string CreateUniqueName()
        {
            var number = _uniqueNameNumber.GetAndIncrement();
            return "$$" + number.Base64Encode();
            //Looking at the scala code, this might not be obvious that the name starts with $$
            //object TestActorRef (TestActorRef.scala) contain this code: 
            //    private[testkit] def randomName: String = {
            //      val l = number.getAndIncrement()
            //      "$" + akka.util.Helpers.base64(l)
            //    }
            //So it adds one $. The second is added by akka.util.Helpers.base64(l) which by default 
            //creates a StringBuilder and adds adds $. Hence, 2 $$
        }
        private InternalTestActorRef(ActorSystem system, Props props, MessageDispatcher dispatcher, Func<Mailbox> createMailbox, IInternalActorRef supervisor, ActorPath path) //TODO: switch from  Func<Mailbox> createMailbox to MailboxType mailboxType      
            : base(system, props, dispatcher, createMailbox, supervisor, path, actorRef => NewActorCell(system, actorRef, props, dispatcher, supervisor, createMailbox))
        {
        }

        protected static ActorCell NewActorCell(ActorSystem system, LocalActorRef actorRef, Props props, MessageDispatcher dispatcher, IInternalActorRef supervisor, Func<Mailbox> createMailbox)
        {
            var cell = new TestActorCell((ActorSystemImpl)system, actorRef, props, dispatcher, supervisor);
            cell.Init(sendSupervise: true, createMailbox: createMailbox);
            return cell;
        }



        protected TestActorCell GetTestActorCell()
        {
            return (TestActorCell)Cell;
        }


        /// <summary>
        /// Directly inject messages into actor receive behavior. Any exceptions
        /// thrown will be available to you, while still being able to use
        /// become/unbecome.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="sender">The sender.</param>
        public void Receive(object message, IActorRef sender = null)
        {
            var cell = Cell;
            sender = sender.IsNobody() ? cell.System.DeadLetters : sender;
            var envelope = new Envelope { Message = message, Sender = sender };
            cell.UseThreadContext(() => cell.ReceiveMessageForTest(envelope));
        }

        public object UnderlyingActor
        {
            get
            {
                if(IsTerminated)
                    throw new IllegalActorStateException("Underlying actor is terminated");
                var actor = GetTestActorCell().Actor;
                if(actor == null)
                {
                    var timeout = TestKitExtension.For(System).DefaultTimeout;
                    actor = this.Ask(InternalGetActor.Instance, timeout).Result;
                }
                return actor;
            }
        }

        /// <summary>
        /// Registers this actor to be a death monitor of the provided ActorRef
        /// This means that this actor will get a Terminated()-message when the provided actor
        /// is permanently terminated.
        /// Returns the same ActorRef that is provided to it, to allow for cleaner invocations.
        /// </summary>
        /// <param name="subject">The subject to watch.</param>
        /// <returns>Returns the same ActorRef that is provided to it, to allow for cleaner invocations.</returns>
        public void Watch(IActorRef subject)
        {
            Cell.Watch(subject);
        }

        /// <summary>
        /// Deregisters this actor from being a death monitor of the provided ActorRef
        /// This means that this actor will not get a Terminated()-message when the provided actor
        /// is permanently terminated.
        /// Returns the same ActorRef that is provided to it, to allow for cleaner invocations.
        /// </summary>
        /// <returns>Returns the same ActorRef that is provided to it, to allow for cleaner invocations.</returns>
        /// <param name="subject">The subject to unwatch.</param>
        public void Unwatch(IActorRef subject)
        {
            Cell.Unwatch(subject);
        }

        public override string ToString()
        {
            return "TestActor[" + Path + "]";
        }
        /// <summary>
        /// INTERNAL
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        /// </summary>
        public static InternalTestActorRef Create(ActorSystem system, Props props, IActorRef supervisor = null, string name = null)
        {
            if(name == null)
                name = CreateUniqueName();

            if(supervisor == null)
            {
                var systemImpl = (ActorSystemImpl)system;
                supervisor = systemImpl.Guardian;
            }


            if(props.Deploy.Dispatcher == Deploy.NoDispatcherGiven)
            {
                props = props.WithDispatcher(CallingThreadDispatcher.Id);
            }

            var dispatcher = system.Dispatchers.Lookup(props.Deploy.Dispatcher);

            var supervisorLocal = supervisor as LocalActorRef;
            if(supervisorLocal != null)
            {
                supervisorLocal.Cell.ReserveChild(name);
            }
            else
            {
                var supervisorRep = supervisor as RepointableActorRef;
                if(supervisorRep != null)
                {
                    var repUnderlying = supervisorRep.Underlying;
                    if(repUnderlying is UnstartedCell)
                        throw new IllegalStateException("Cannot attach a TestActor to an unstarted top-level actor, ensure that it is started by sending a message and observing the reply");
                    var cellUnderlying = repUnderlying as ActorCell;
                    if(cellUnderlying != null)
                    {
                        cellUnderlying.ReserveChild(name);
                    }
                    else
                    {
                        system.Log.Error("Trying to attach child {0} to unknown type of supervisor cell {1}, this is not going to end well", name, repUnderlying.GetType());
                    }
                }
            }
            //TODO: Should be: Func<Mailbox> mailbox = () => system.Mailboxes.FromConfig(dispatcher.Configurator.Config);
            Func<Mailbox> mailbox = () => system.Mailboxes.CreateMailbox(props, null);
            var testActorRef = new InternalTestActorRef(system, props, dispatcher, mailbox, (IInternalActorRef)supervisor, supervisor.Path / name);

            // we need to start ourselves since the creation of an actor has been split into initialization and starting
            testActorRef.Underlying.Start();
            return testActorRef;
        }


        protected class TestActorCell : ActorCell
        {
            public TestActorCell(ActorSystemImpl system, IInternalActorRef self, Props props, MessageDispatcher dispatcher, IInternalActorRef parent)
                : base(system, self, props, dispatcher, parent)
            {
            }

            protected override void AutoReceiveMessage(Envelope envelope)
            {
                if(envelope.Message is InternalGetActor)
                    Sender.Tell(Actor, Self);
                else
                    base.AutoReceiveMessage(envelope);
            }
            public new object Actor { get { return base.Actor; } }
        }


        public class InternalGetActor : IAutoReceivedMessage, IPossiblyHarmful
        {
            public static readonly InternalGetActor Instance = new InternalGetActor();
            private InternalGetActor() { }
        }
    }
}

