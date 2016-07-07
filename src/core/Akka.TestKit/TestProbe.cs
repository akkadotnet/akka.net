//-----------------------------------------------------------------------
// <copyright file="TestProbe.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Dispatch.SysMsg;
using Akka.Util;

namespace Akka.TestKit
{
    /// <summary>
    /// TestKit-based probe which allows sending, reception and reply.
    /// Use <see cref="TestKitBase.CreateTestProbe(string)" /> inside your test 
    /// to create new instances.
    /// </summary>
    public class TestProbe : TestKitBase, INoImplicitSender, IInternalActorRef
    {      
        public TestProbe(ActorSystem system, ITestKitAssertions assertions, string testProbeName=null)
            : base(assertions, system, testProbeName)
        {
        }

        /// <summary>Gets the reference of this probe.</summary>
        public IActorRef Ref { get { return TestActor; } }

        /// <summary>Gets the sender of the last message</summary>
        public IActorRef Sender { get { return LastSender; } }

        /// <summary>
        /// Send message to an actor while using the probe as the sender.
        /// Replies will be available for inspection with all of TestKit's assertion
        /// methods.
        /// </summary>
        /// <param name="actor">The actor.</param>
        /// <param name="message">The message.</param>
        public void Send(IActorRef actor, object message)
        {
            actor.Tell(message, TestActor);
        }


        /// <summary>
        /// Forwards a message to the specified actor. As sender the sender of the last message is used.
        /// </summary>
        /// <param name="actor">The actor to forward to.</param>
        /// <param name="message">The message.</param>
        public void Forward(IActorRef actor, object message)
        {
            actor.Tell(message, Sender);
        }

        /// <summary>
        /// Forwards the last received message to the specified actor as if the 
        /// <see cref="TestKitBase.LastMessage"/> was sent directly to the actor in the first place.
        /// </summary>
        /// <param name="actor">The actor to forward to.</param>
        public void Forward(IActorRef actor)
        {
            actor.Tell(LastMessage, Sender);
        }


        /// <summary>
        /// Send message to the sender of the last received message.
        /// </summary>
        /// <param name="message">The message.</param>
        public void Reply(object message)
        {
            Sender.Tell(message,TestActor);
        }

        [Obsolete("Cannot create a TestProbe from a TestProbe", true)]
        public override TestProbe CreateTestProbe(string name=null)
        {
            throw new NotSupportedException("Cannot create a TestProbe from a TestProbe");
        }

        int IComparable<IActorRef>.CompareTo(IActorRef other)
        {
            return TestActor.CompareTo(other);
        }

        bool IEquatable<IActorRef>.Equals(IActorRef other)
        {
            return TestActor.Equals(other);
        }

        ActorPath IActorRef.Path { get { return TestActor.Path; } }

        void ICanTell.Tell(object message, IActorRef sender)
        {
            TestActor.Tell(message, sender);
        }

        ISurrogate ISurrogated.ToSurrogate(ActorSystem system)
        {
            return TestActor.ToSurrogate(system);
        }

        bool IActorRefScope.IsLocal { get { return ((IInternalActorRef) TestActor).IsLocal; } }

        IInternalActorRef IInternalActorRef.Parent { get { return ((IInternalActorRef)TestActor).Parent; } }

        IActorRefProvider IInternalActorRef.Provider { get { return ((IInternalActorRef)TestActor).Provider; } }

        bool IInternalActorRef.IsTerminated { get { return ((IInternalActorRef)TestActor).IsTerminated; } }

        IActorRef IInternalActorRef.GetChild(IEnumerable<string> name)
        {
            return ((IInternalActorRef)TestActor).GetChild(name);
        }

        void IInternalActorRef.Resume(Exception causedByFailure)
        {
            ((IInternalActorRef)TestActor).Resume(causedByFailure);
        }

        void IInternalActorRef.Start()
        {
            ((IInternalActorRef)TestActor).Start();
        }

        void IInternalActorRef.Stop()
        {
            ((IInternalActorRef)TestActor).Stop();
        }

        void IInternalActorRef.Restart(Exception cause)
        {
            ((IInternalActorRef)TestActor).Restart(cause);
        }

        void IInternalActorRef.Suspend()
        {
            ((IInternalActorRef)TestActor).Suspend();
        }

        public void SendSystemMessage(ISystemMessage message, IActorRef sender)
        {
            ((IInternalActorRef)TestActor).SendSystemMessage(message);
        }

        public void SendSystemMessage(ISystemMessage message)
        {
            ((IInternalActorRef)TestActor).SendSystemMessage(message);
        }

        public int CompareTo(object obj)
        {
            return TestActor.CompareTo(obj);
        }
    }
}

