using System;
using System.Linq.Expressions;
using Akka.Actor;

namespace Akka.TestKit
{
    public static class TestActorRef
    {
        public static TestActorRef<T> Create<T>(ActorSystem system, Props props, ActorRef supervisor = null, string name = null) where T : ActorBase
        {
            return new TestActorRef<T>(system, props, supervisor, name);
        }

        public static TestActorRef<T> Create<T>(ActorSystem system, Props props, string name) where T : ActorBase
        {
            return Create<T>(system, props, null, name);
        }

        public static TestActorRef<T> Create<T>(ActorSystem system, Expression<Func<T>> factory, string name = null) where T : ActorBase
        {
            return Create<T>(system, Props.Create(factory), null, name);
        }

        public static TestActorRef<T> Create<T>(ActorSystem system, Expression<Func<T>> factory, ActorRef supervisor, string name = null) where T : ActorBase
        {
            return Create<T>(system, Props.Create(factory), supervisor, name);
        }

        public static TestActorRef<T> Create<T>(ActorSystem system, ActorRef supervisor, string name = null) where T : ActorBase, new()
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
    /// overrides the dispatcher to <see cref="CallingThreadDispatcher"/> and sets the receiveTimeout to None. Otherwise,
    /// it acts just like a normal ActorRef. You may retrieve a reference to the underlying actor to test internal logic.
    /// A <see cref="TestActorRef{TActor}"/> can be implicitly casted to an <see cref="ActorRef"/> or you can get the actual
    /// <see cref="ActorRef"/> from the <see cref="TestActorRefBase{TActor}.Ref">Ref</see> property.
    /// </summary>
    /// <typeparam name="TActor">The type of actor</typeparam>
    public class TestActorRef<TActor> : TestActorRefBase<TActor> where TActor : ActorBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TestActorRef{TActor}"/> class.
        /// </summary>
        /// <param name="system">The system.</param>
        /// <param name="actorProps">The actor props.</param>
        /// <param name="supervisor">The supervisor.</param>
        /// <param name="name">The name.</param>
        public TestActorRef(ActorSystem system, Props actorProps, ActorRef supervisor = null, string name = null) : base(system, actorProps, supervisor, name)
        {
        }

        public static bool operator ==(TestActorRef<TActor> testActorRef, ActorRef actorRef)
        {
            if(ReferenceEquals(testActorRef, null)) return ReferenceEquals(actorRef, null);
            return testActorRef.Equals(actorRef);
        }

        public static bool operator !=(TestActorRef<TActor> testActorRef, ActorRef actorRef)
        {
            if(ReferenceEquals(testActorRef, null)) return !ReferenceEquals(actorRef, null);
            return !testActorRef.Equals(actorRef);
        }

        public static bool operator ==(ActorRef actorRef, TestActorRef<TActor> testActorRef)
        {
            if(ReferenceEquals(testActorRef, null)) return ReferenceEquals(actorRef, null);
            return testActorRef.Equals(actorRef);
        }

        public static bool operator !=(ActorRef actorRef, TestActorRef<TActor> testActorRef)
        {
            if(ReferenceEquals(testActorRef, null)) return !ReferenceEquals(actorRef, null);
            return !testActorRef.Equals(actorRef);
        }

        public static implicit operator ActorRef(TestActorRef<TActor> actorRef)
        {
            return actorRef.Ref;
        }

        //Here to suppress CS0660, 'class' defines operator == or operator != but does not override Object.Equals(object o)
        public override bool Equals(object obj)
        {
            //We have correct implementations in TestActorRefBase, so it's perfectly fine to delegate
            return base.Equals(obj);
        }

        //Here to suppress CS0661, 'class' defines operator == or operator != but does not override Object.GetHashCode()
        public override int GetHashCode()
        {
            //We have correct implementations in TestActorRefBase, so it's perfectly fine to delegate
            return base.GetHashCode();
        }
    }
}