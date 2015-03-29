using System;
using Akka.Actor;
using Akka.Dispatch;
using Akka.TestKit.Internal;
using Akka.Util;

namespace Akka.TestKit
{
    /// <summary>
    /// This is the base class for TestActorRefs
    /// </summary>
    /// <typeparam name="TActor">The type of actor</typeparam>
    public abstract class TestActorRefBase<TActor> : ICanTell, IEquatable<ActorRef>, ActorRef where TActor : ActorBase
    {
        private readonly InternalTestActorRef _internalRef;

        protected TestActorRefBase(ActorSystem system, Props actorProps, ActorRef supervisor=null, string name=null)
        {
            _internalRef = InternalTestActorRef.Create(system, actorProps, supervisor, name);
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
            _internalRef.Receive(message, sender);
        }

        public ActorRef Ref
        {
            get { return _internalRef; }
        }

        protected InternalTestActorRef InternalRef
        {
            get { return _internalRef; }
        }

        public TActor UnderlyingActor
        {
            get { return (TActor) _internalRef.UnderlyingActor; }
        }

        /// <summary>
        /// Gets the path of this instance
        /// </summary>
        public ActorPath Path { get { return _internalRef.Path; } }

        /// <summary>
        /// Sends a message to this actor. 
        /// If this call is made from within an actor, the current actor will be the sender.
        /// If the call is made from a test class that is based on TestKit, TestActor will 
        /// will be the sender;
        /// otherwise <see cref="NoSender"/> will be set as sender.
        /// </summary>
        /// <param name="message">The message.</param>
        public void Tell(object message)
        {
            _internalRef.Tell(message);
        }


        /// <summary>
        /// Forwards a message to this actor.
        /// If this call is made from within an actor, the current actor will be the sender.
        /// If the call is made from a test class that is based on TestKit, TestActor will 
        /// will be the sender;
        /// </summary>
        /// <param name="message">The message.</param>
        public void Forward(object message)
        {
            _internalRef.Forward(message);
        }

        /// <summary>
        /// Sends a message to this actor with the specified sender.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="sender">The sender</param>
        public void Tell(object message, ActorRef sender)
        {
            _internalRef.Tell(message, sender);

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
            _internalRef.Watch(subject);
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
            _internalRef.Unwatch(subject);
        }

        public override string ToString()
        {
            return _internalRef.ToString();
        }

        protected delegate TActorRef CreateTestActorRef<out TActorRef>(ActorSystem system, Props props, MessageDispatcher dispatcher, Func<Mailbox> mailbox, InternalActorRef supervisor, ActorPath path) where TActorRef : TestActorRefBase<TActor>;

        public override bool Equals(object obj)
        {
            return _internalRef.Equals(obj);
        }

        public override int GetHashCode()
        {
            return _internalRef.GetHashCode();
        }

        public bool Equals(ActorRef other)
        {
            return _internalRef.Equals(other);
        }

        public static bool operator ==(TestActorRefBase<TActor> testActorRef, ActorRef actorRef)
        {
            if(ReferenceEquals(testActorRef, null)) return ReferenceEquals(actorRef, null);
            return testActorRef.Equals(actorRef);
        }

        public static bool operator !=(TestActorRefBase<TActor> testActorRef, ActorRef actorRef)
        {
            if(ReferenceEquals(testActorRef, null)) return !ReferenceEquals(actorRef, null);
            return !testActorRef.Equals(actorRef);
        }

        public static bool operator ==(ActorRef actorRef, TestActorRefBase<TActor> testActorRef)
        {
            if(ReferenceEquals(testActorRef, null)) return ReferenceEquals(actorRef, null);
            return testActorRef.Equals(actorRef);
        }

        public static bool operator !=(ActorRef actorRef, TestActorRefBase<TActor> testActorRef)
        {
            if(ReferenceEquals(testActorRef, null)) return !ReferenceEquals(actorRef, null);
            return !testActorRef.Equals(actorRef);
        }

        public static ActorRef ToActorRef(TestActorRefBase<TActor> actorRef)
        {
            return actorRef._internalRef;
        }

        //ActorRef implementations
        int IComparable<ActorRef>.CompareTo(ActorRef other)
        {
            return Ref.CompareTo(other);
        }

        bool IEquatable<ActorRef>.Equals(ActorRef other)
        {
            return Ref.Equals(other);
        }

        ActorPath ActorRef.Path { get { return Ref.Path; } }

        void ICanTell.Tell(object message, ActorRef sender)
        {
            Ref.Tell(message, sender);
        }

        ISurrogate ISurrogated.ToSurrogate(ActorSystem system)
        {
            return Ref.ToSurrogate(system);
        }
    }
}