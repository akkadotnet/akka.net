//-----------------------------------------------------------------------
// <copyright file="TestActorRefBase.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Dispatch.SysMsg;
using Akka.TestKit.Internal;
using Akka.Util;

namespace Akka.TestKit
{
    /// <summary>
    /// This is the base class for TestActorRefs
    /// </summary>
    /// <typeparam name="TActor">The type of actor</typeparam>
    public abstract class TestActorRefBase<TActor> : ICanTell, IEquatable<IActorRef>, IInternalActorRef where TActor : ActorBase
    {
        private readonly InternalTestActorRef _internalRef;

        protected TestActorRefBase(ActorSystem system, Props actorProps, IActorRef supervisor=null, string name=null)
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
        public void Receive(object message, IActorRef sender = null)
        {
            _internalRef.Receive(message, sender);
        }

        public IActorRef Ref
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
        /// otherwise <see cref="ActorRefs.NoSender"/> will be set as sender.
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
        public void Tell(object message, IActorRef sender)
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
        public void Watch(IActorRef subject)
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
        public void Unwatch(IActorRef subject)
        {
            _internalRef.Unwatch(subject);
        }

        public override string ToString()
        {
            return _internalRef.ToString();
        }

        protected delegate TActorRef CreateTestActorRef<out TActorRef>(ActorSystem system, Props props, MessageDispatcher dispatcher, Func<Mailbox> mailbox, IInternalActorRef supervisor, ActorPath path) where TActorRef : TestActorRefBase<TActor>;

        public override bool Equals(object obj)
        {
            return _internalRef.Equals(obj);
        }

        public override int GetHashCode()
        {
            return _internalRef.GetHashCode();
        }

        public int CompareTo(object obj)
        {
            return ((IComparable) _internalRef).CompareTo(obj);
        }

        public bool Equals(IActorRef other)
        {
            return _internalRef.Equals(other);
        }

        public static bool operator ==(TestActorRefBase<TActor> testActorRef, IActorRef actorRef)
        {
            if(ReferenceEquals(testActorRef, null)) return ReferenceEquals(actorRef, null);
            return testActorRef.Equals(actorRef);
        }

        public static bool operator !=(TestActorRefBase<TActor> testActorRef, IActorRef actorRef)
        {
            if(ReferenceEquals(testActorRef, null)) return !ReferenceEquals(actorRef, null);
            return !testActorRef.Equals(actorRef);
        }

        public static bool operator ==(IActorRef actorRef, TestActorRefBase<TActor> testActorRef)
        {
            if(ReferenceEquals(testActorRef, null)) return ReferenceEquals(actorRef, null);
            return testActorRef.Equals(actorRef);
        }

        public static bool operator !=(IActorRef actorRef, TestActorRefBase<TActor> testActorRef)
        {
            if(ReferenceEquals(testActorRef, null)) return !ReferenceEquals(actorRef, null);
            return !testActorRef.Equals(actorRef);
        }

        public static IActorRef ToActorRef(TestActorRefBase<TActor> actorRef)
        {
            return actorRef._internalRef;
        }

        //ActorRef implementations
        int IComparable<IActorRef>.CompareTo(IActorRef other)
        {
            return _internalRef.CompareTo(other);
        }

        bool IEquatable<IActorRef>.Equals(IActorRef other)
        {
            return _internalRef.Equals(other);
        }

        ActorPath IActorRef.Path { get { return _internalRef.Path; } }

        void ICanTell.Tell(object message, IActorRef sender)
        {
            _internalRef.Tell(message, sender);
        }

        ISurrogate ISurrogated.ToSurrogate(ActorSystem system)
        {
            return _internalRef.ToSurrogate(system);
        }

        bool IActorRefScope.IsLocal { get { return _internalRef.IsLocal; } }

        IInternalActorRef IInternalActorRef.Parent { get { return _internalRef.Parent; } }

        IActorRefProvider IInternalActorRef.Provider { get { return _internalRef.Provider; } }

        bool IInternalActorRef.IsTerminated { get { return _internalRef.IsTerminated; } }

        IActorRef IInternalActorRef.GetChild(IEnumerable<string> name)
        {
            return _internalRef.GetChild(name);
        }

        void IInternalActorRef.Resume(Exception causedByFailure)
        {
            _internalRef.Resume(causedByFailure);
        }

        void IInternalActorRef.Start()
        {
            _internalRef.Start();
        }

        void IInternalActorRef.Stop()
        {
            _internalRef.Stop();
        }

        void IInternalActorRef.Restart(Exception cause)
        {
            _internalRef.Restart(cause);
        }

        void IInternalActorRef.Suspend()
        {
            _internalRef.Suspend();
        }

        public void SendSystemMessage(ISystemMessage message, IActorRef sender)
        {
            _internalRef .SendSystemMessage(message, sender);
        }
    }
}

