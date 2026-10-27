//-----------------------------------------------------------------------
// <copyright file="TestActorRefBase.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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

        /// <summary>
        /// Creates a test actor reference with the specified actor system, properties, supervisor, and name.
        /// </summary>
        /// <param name="system">The actor system that owns the test actor.</param>
        /// <param name="actorProps">The properties used to create the actor.</param>
        /// <param name="supervisor">The supervising actor, or null to use the system guardian.</param>
        /// <param name="name">The actor name, or null to generate a unique name.</param>
        protected TestActorRefBase(ActorSystem system, Props actorProps, IActorRef supervisor=null, string name=null)
        {
            _internalRef = InternalTestActorRef.Create(system, actorProps, supervisor, name);
        }

        /// <summary>
        /// Directly inject messages into actor receive behavior. Any exceptions
        /// thrown will be available to you, while still being able to use
        /// become/unbecome.
        /// Note: This method violates the actor model and could cause unpredictable 
        /// behavior. For example, a Receive call to an actor could run simultaneously 
        /// (2 simultaneous threads running inside the actor) with the actor's handling 
        /// of a previous Tell call. 
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="sender">The sender.</param>
        public void Receive(object message, IActorRef sender = null)
        {
            _internalRef.Receive(message, sender);
        }

        /// <summary>
        /// Directly inject messages into actor ReceiveAsync behavior. Any exceptions
        /// thrown will be available to you, while still being able to use
        /// become/unbecome.
        /// Note: This method violates the actor model and could cause unpredictable 
        /// behavior. For example, a Receive call to an actor could run simultaneously 
        /// (2 simultaneous threads running inside the actor) with the actor's handling 
        /// of a previous Tell call. 
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="sender">The sender.</param>
        public Task ReceiveAsync(object message, IActorRef sender = null)
        {
            return _internalRef.ReceiveAsync(message, sender);
        }
        
        /// <summary>
        /// Gets the underlying actor reference.
        /// </summary>
        public IActorRef Ref
        {
            get { return _internalRef; }
        }

        /// <summary>
        /// Gets the internal test actor reference used by this wrapper.
        /// </summary>
        protected InternalTestActorRef InternalRef
        {
            get { return _internalRef; }
        }

        /// <summary>
        /// Gets the underlying actor instance for direct inspection in a test.
        /// </summary>
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

        /// <summary>
        /// Compares a specified <see cref="TestActorRefBase{TActor}"/> to an <see cref="IActorRef"/> for equality.
        /// </summary>
        /// <param name="testActorRef">The test actor used for comparison</param>
        /// <param name="actorRef">The actor used for comparison</param>
        /// <returns><c>true</c> if both actors are equal; otherwise <c>false</c></returns>
        public static bool operator ==(TestActorRefBase<TActor> testActorRef, IActorRef actorRef)
        {
            if(ReferenceEquals(testActorRef, null)) return ReferenceEquals(actorRef, null);
            return testActorRef.Equals(actorRef);
        }

        /// <summary>
        /// Compares a specified <see cref="TestActorRefBase{TActor}"/> to an <see cref="IActorRef"/> for inequality.
        /// </summary>
        /// <param name="testActorRef">The test actor used for comparison</param>
        /// <param name="actorRef">The actor used for comparison</param>
        /// <returns><c>true</c> if both actors are not equal; otherwise <c>false</c></returns>
        public static bool operator !=(TestActorRefBase<TActor> testActorRef, IActorRef actorRef)
        {
            if(ReferenceEquals(testActorRef, null)) return !ReferenceEquals(actorRef, null);
            return !testActorRef.Equals(actorRef);
        }

        /// <summary>
        /// Compares a specified <see cref="IActorRef"/> to an <see cref="TestActorRefBase{TActor}"/> for equality.
        /// </summary>
        /// <param name="actorRef">The actor used for comparison</param>
        /// <param name="testActorRef">The test actor used for comparison</param>
        /// <returns><c>true</c> if both actors are equal; otherwise <c>false</c></returns>
        public static bool operator ==(IActorRef actorRef, TestActorRefBase<TActor> testActorRef)
        {
            if(ReferenceEquals(testActorRef, null)) return ReferenceEquals(actorRef, null);
            return testActorRef.Equals(actorRef);
        }

        /// <summary>
        /// Compares a specified <see cref="IActorRef"/> to an <see cref="TestActorRefBase{TActor}"/> for inequality.
        /// </summary>
        /// <param name="actorRef">The actor used for comparison</param>
        /// <param name="testActorRef">The test actor used for comparison</param>
        /// <returns><c>true</c> if both actors are not equal; otherwise <c>false</c></returns>
        public static bool operator !=(IActorRef actorRef, TestActorRefBase<TActor> testActorRef)
        {
            if(ReferenceEquals(testActorRef, null)) return !ReferenceEquals(actorRef, null);
            return !testActorRef.Equals(actorRef);
        }

        /// <summary>
        /// Converts a test actor reference to its underlying actor reference.
        /// </summary>
        /// <param name="actorRef">The test actor reference to convert.</param>
        /// <returns>The actor reference represented by <paramref name="actorRef"/>.</returns>
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

        IActorRef IInternalActorRef.GetChild(IReadOnlyList<string> name)
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

        /// <summary>
        /// Sends a system message to the underlying test actor reference.
        /// </summary>
        /// <param name="message">The system message to send.</param>
        /// <param name="sender">The sender argument from the actor-reference contract; this implementation does not use it.</param>
        public void SendSystemMessage(ISystemMessage message, IActorRef sender)
        {
            _internalRef.SendSystemMessage(message);
        }

        /// <summary>
        /// Sends a system message to the underlying test actor reference.
        /// </summary>
        /// <param name="message">The system message to send.</param>
        public void SendSystemMessage(ISystemMessage message)
        {
            _internalRef.SendSystemMessage(message);
        }
    }
}
