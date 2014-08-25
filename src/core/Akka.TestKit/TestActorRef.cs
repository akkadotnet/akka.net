using System;
using System.Linq.Expressions;
using Akka.Actor;
using Akka.Actor.Internals;
using Akka.Dispatch;
using Akka.Pattern;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.TestKit
{
    public static class TestActorRef
    {
        private static AtomicCounterLong _number = new AtomicCounterLong(0);

        private static string CreateUniqueName()
        {
            var number = _number.GetAndIncrement();
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

        public static TestActorRef<T> Create<T>(ActorSystem system, Props props, ActorRef supervisor = null, string name = null) where T : ActorBase
        {
            var systemImpl = (ActorSystemImpl)system;
            return TestActorRef<T>.Create(systemImpl, props, supervisor ?? systemImpl.Guardian, name ?? CreateUniqueName());
        }

        public static TestActorRef<T> Create<T>(ActorSystem system, Props props, string name) where T : ActorBase
        {
            return Create<T>(system, props, null, name);
        }

        public static TestActorRef<T> Create<T>(ActorSystem system, Expression<Func<T>> factory, string name = null) where T : ActorBase
        {
            return Create<T>(system, Props.Create(factory), null, name);
        }

        public static TestActorRef<T> Create<T>(ActorSystem system,  ActorRef supervisor, string name = null) where T : ActorBase, new()
        {
            return Create<T>(system, Props.Create<T>(), supervisor, name);
        }
        public static TestActorRef<T> Create<T>(ActorSystem system, string name = null) where T : ActorBase, new()
        {
            return Create<T>(system, Props.Create<T>(), null, name);
        }


        public class InternalGetActor : AutoReceivedMessage, PossiblyHarmful
        {
            public static InternalGetActor Instance = new InternalGetActor();
            private InternalGetActor() { }

        }
    }

    /// <summary>
    /// This special ActorRef is exclusively for use during unit testing in a single-threaded environment. Therefore, it
    /// overrides the dispatcher to CallingThreadDispatcher and sets the receiveTimeout to None. Otherwise,
    /// it acts just like a normal ActorRef. You may retrieve a reference to the underlying actor to test internal logic.
    /// </summary>
    /// <typeparam name="T">The type of actor</typeparam>
    public class TestActorRef<T> : LocalActorRef where T : ActorBase
    {
        private TestActorRef(ActorSystem system, Props props, MessageDispatcher dispatcher, Func<Mailbox> createMailbox, InternalActorRef supervisor, ActorPath path) //TODO: switch from  Func<Mailbox> createMailbox to MailboxType mailboxType      
            : base(system, props, dispatcher, createMailbox, supervisor, path, actorRef => NewActorCell(system, actorRef, props, dispatcher, supervisor, createMailbox))
        {
        }

        protected static ActorCell NewActorCell(ActorSystem system, LocalActorRef actorRef, Props props, MessageDispatcher dispatcher, InternalActorRef supervisor, Func<Mailbox> createMailbox)
        {
            var cell = new TestActorCell((ActorSystemImpl) system, actorRef, props, dispatcher, supervisor);
            cell.Init(sendSupervise: false, createMailbox: createMailbox);
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
        public void Receive(object message, ActorRef sender = null)
        {
            var cell = Cell;
            sender = sender.IsNobody() ? cell.System.DeadLetters : sender;
            var envelope = new Envelope() { Message = message, Sender = sender };
            cell.UseThreadContext(()=>cell.ReceiveMessageForTest(envelope));
        }

        public T UnderlyingActor
        {
            get
            {
                if(IsTerminated)
                    throw new IllegalActorStateException("Underlying actor is terminated");
                var actor = GetTestActorCell().Actor;
                if(actor == null)
                {
                    var timeout = TestKitExtension.For(System).DefaultTimeout;
                    actor = this.Ask<T>(TestActorRef.InternalGetActor.Instance, timeout).Result;
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
        public void Watch(ActorRef subject)
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
        public void Unwatch(ActorRef subject)
        {
            Cell.Unwatch(subject);
        }

        public override string ToString()
        {
            return "TestActor[" + Path + "]";
        }

        public static TestActorRef<T> Create(ActorSystem system, Props props, ActorRef supervisor, string name)
        {
            if(props.Deploy.Dispatcher == Deploy.NoDispatcherGiven)
            {
                props = props.WithDispatcher(CallingThreadDispatcher.Id);
            }

            //TODO: Should be val dispatcher = _system.dispatchers.lookup(props.dispatcher) 
            //      but since we don't have the CallingThreadDispatcherConfigurator yet, we'll just create the dispatcher manually
            var dispatcher = props.Deploy.Dispatcher == CallingThreadDispatcher.Id ? new CallingThreadDispatcher() : system.Dispatchers.FromConfig(props.Dispatcher);

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
            Func<Mailbox> mailbox = () => system.Mailboxes.FromConfig(props.Mailbox);
            var testActorRef = new TestActorRef<T>(system, props, dispatcher, mailbox, (InternalActorRef)supervisor, supervisor.Path / name);

            // we need to start ourselves since the creation of an actor has been split into initialization and starting
            testActorRef.Underlying.Start();
            return testActorRef;
        }


        protected class TestActorCell : ActorCell
        {
            public TestActorCell(ActorSystemImpl system, InternalActorRef self, Props props, MessageDispatcher dispatcher, InternalActorRef parent)
                : base(system, self, props, dispatcher, parent)
            {
            }

            protected override void AutoReceiveMessage(Envelope envelope)
            {
                if(envelope.Message is TestActorRef.InternalGetActor)
                    Sender.Tell(Actor, Self);
                else
                    base.AutoReceiveMessage(envelope);
            }

            public new T Actor { get { return (T)base.Actor; } }
        }
    }
}