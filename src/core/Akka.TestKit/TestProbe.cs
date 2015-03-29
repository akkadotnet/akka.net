using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Util;

namespace Akka.TestKit
{
    /// <summary>
    /// TestKit-based probe which allows sending, reception and reply.
    /// Use <see cref="TestKitBase.CreateTestProbe(string)" /> inside your test 
    /// to create new instances.
    /// </summary>
    public class TestProbe : TestKitBase, NoImplicitSender, InternalActorRef
    {      
        public TestProbe(ActorSystem system, TestKitAssertions assertions, string testProbeName=null)
            : base(assertions, system, testProbeName)
        {
        }

        /// <summary>Gets the reference of this probe.</summary>
        public ActorRef Ref { get { return TestActor; } }

        /// <summary>Gets the sender of the last message</summary>
        public ActorRef Sender { get { return LastSender; } }

        /// <summary>
        /// Send message to an actor while using the probe as the sender.
        /// Replies will be available for inspection with all of TestKit's assertion
        /// methods.
        /// </summary>
        /// <param name="actor">The actor.</param>
        /// <param name="message">The message.</param>
        public void Send(ActorRef actor, object message)
        {
            actor.Tell(message, TestActor);
        }


        /// <summary>
        /// Forwards a message to the specified actor. As sender the sender of the last message is used.
        /// </summary>
        /// <param name="actor">The actor to forward to.</param>
        /// <param name="message">The message.</param>
        public void Forward(ActorRef actor, object message)
        {
            actor.Tell(message, Sender);
        }

        /// <summary>
        /// Forwards the last received message to the specified actor as if the 
        /// <see cref="TestKitBase.LastMessage"/> was sent directly to the actor in the first place.
        /// </summary>
        /// <param name="actor">The actor to forward to.</param>
        public void Forward(ActorRef actor)
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

        int IComparable<ActorRef>.CompareTo(ActorRef other)
        {
            return TestActor.CompareTo(other);
        }

        bool IEquatable<ActorRef>.Equals(ActorRef other)
        {
            return TestActor.Equals(other);
        }

        ActorPath ActorRef.Path { get { return TestActor.Path; } }

        void ICanTell.Tell(object message, ActorRef sender)
        {
            TestActor.Tell(message, sender);
        }

        ISurrogate ISurrogated.ToSurrogate(ActorSystem system)
        {
            return TestActor.ToSurrogate(system);
        }

        bool ActorRefScope.IsLocal { get { return ((InternalActorRef) TestActor).IsLocal; } }

        InternalActorRef InternalActorRef.Parent { get { return ((InternalActorRef)TestActor).Parent; } }

        ActorRefProvider InternalActorRef.Provider { get { return ((InternalActorRef)TestActor).Provider; } }

        bool InternalActorRef.IsTerminated { get { return ((InternalActorRef)TestActor).IsTerminated; } }

        ActorRef InternalActorRef.GetChild(IEnumerable<string> name)
        {
            return ((InternalActorRef)TestActor).GetChild(name);
        }

        void InternalActorRef.Resume(Exception causedByFailure)
        {
            ((InternalActorRef)TestActor).Resume(causedByFailure);
        }

        void InternalActorRef.Start()
        {
            ((InternalActorRef)TestActor).Start();
        }

        void InternalActorRef.Stop()
        {
            ((InternalActorRef)TestActor).Stop();
        }

        void InternalActorRef.Restart(Exception cause)
        {
            ((InternalActorRef)TestActor).Restart(cause);
        }

        void InternalActorRef.Suspend()
        {
            ((InternalActorRef)TestActor).Suspend();
        }
    }
}
