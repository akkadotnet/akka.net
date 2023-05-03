//-----------------------------------------------------------------------
// <copyright file="TestActorRefBase.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <param name="actorProps">TBD</param>
        /// <param name="supervisor">TBD</param>
        /// <param name="name">TBD</param>
        protected TestActorRefBase(ActorSystem system, Props actorProps, IActorRef supervisor=null, string name=null)
        {
            InternalRef = InternalTestActorRef.Create(system, actorProps, supervisor, name);
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
            InternalRef.Receive(message, sender);
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
            return InternalRef.ReceiveAsync(message, sender);
        }
        
        /// <summary>
        /// TBD
        /// </summary>
        public IActorRef Ref
        {
            get { return InternalRef; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected InternalTestActorRef InternalRef { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public TActor UnderlyingActor
        {
            get { return (TActor) InternalRef.UnderlyingActor; }
        }

        /// <summary>
        /// Gets the path of this instance
        /// </summary>
        public ActorPath Path { get { return InternalRef.Path; } }

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
            InternalRef.Tell(message);
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
            InternalRef.Forward(message);
        }

        /// <summary>
        /// Sends a message to this actor with the specified sender.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="sender">The sender</param>
        public void Tell(object message, IActorRef sender)
        {
            InternalRef.Tell(message, sender);

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
            InternalRef.Watch(subject);
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
            InternalRef.Unwatch(subject);
        }

       
        public override string ToString()
        {
            return InternalRef.ToString();
        }

        
        public override bool Equals(object obj)
        {
            return InternalRef.Equals(obj);
        }

        
        public override int GetHashCode()
        {
            return InternalRef.GetHashCode();
        }

        
        public int CompareTo(object obj)
        {
            return ((IComparable) InternalRef).CompareTo(obj);
        }

        
        public bool Equals(IActorRef other)
        {
            return InternalRef.Equals(other);
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
        /// TBD
        /// </summary>
        /// <param name="actorRef">TBD</param>
        /// <returns>TBD</returns>
        public static IActorRef ToActorRef(TestActorRefBase<TActor> actorRef)
        {
            return actorRef.InternalRef;
        }

        //ActorRef implementations
        int IComparable<IActorRef>.CompareTo(IActorRef other)
        {
            return InternalRef.CompareTo(other);
        }

        bool IEquatable<IActorRef>.Equals(IActorRef other)
        {
            return InternalRef.Equals(other);
        }

        ActorPath IActorRef.Path { get { return InternalRef.Path; } }

        void ICanTell.Tell(object message, IActorRef sender)
        {
            InternalRef.Tell(message, sender);
        }

        ISurrogate ISurrogated.ToSurrogate(ActorSystem system)
        {
            return InternalRef.ToSurrogate(system);
        }

        bool IActorRefScope.IsLocal { get { return InternalRef.IsLocal; } }

        IInternalActorRef IInternalActorRef.Parent { get { return InternalRef.Parent; } }

        IActorRefProvider IInternalActorRef.Provider { get { return InternalRef.Provider; } }

        bool IInternalActorRef.IsTerminated { get { return InternalRef.IsTerminated; } }

        IActorRef IInternalActorRef.GetChild(IReadOnlyList<string> name)
        {
            return InternalRef.GetChild(name);
        }

        void IInternalActorRef.Resume(Exception causedByFailure)
        {
            InternalRef.Resume(causedByFailure);
        }

        void IInternalActorRef.Start()
        {
            InternalRef.Start();
        }

        void IInternalActorRef.Stop()
        {
            InternalRef.Stop();
        }

        void IInternalActorRef.Restart(Exception cause)
        {
            InternalRef.Restart(cause);
        }

        void IInternalActorRef.Suspend()
        {
            InternalRef.Suspend();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <param name="sender">TBD</param>
        public void SendSystemMessage(ISystemMessage message, IActorRef sender)
        {
            InternalRef.SendSystemMessage(message);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        public void SendSystemMessage(ISystemMessage message)
        {
            InternalRef.SendSystemMessage(message);
        }
    }
}
