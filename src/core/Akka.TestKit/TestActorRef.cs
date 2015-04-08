using Akka.Actor;

namespace Akka.TestKit
{
    /// <summary>
    /// This special ActorRef is exclusively for use during unit testing in a single-threaded environment. Therefore, it
    /// overrides the dispatcher to <see cref="CallingThreadDispatcher"/> and sets the receiveTimeout to None. Otherwise,
    /// it acts just like a normal ActorRef. You may retrieve a reference to the underlying actor to test internal logic.
    /// A <see cref="TestActorRef{TActor}"/> can be implicitly casted to an <see cref="IActorRef"/> or you can get the actual
    /// <see cref="IActorRef"/> from the <see cref="TestActorRefBase{TActor}.Ref">Ref</see> property.
    /// </summary>
    /// <typeparam name="TActor">The type of actor</typeparam>
    public class TestActorRef<TActor> : TestActorRefBase<TActor> where TActor : ActorBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TestActorRef{TActor}"/> class.
        /// </summary>
        /// <param name="system">The system.</param>
        /// <param name="actorProps">The actor props.</param>
        /// <param name="supervisor">Optional: The supervisor.</param>
        /// <param name="name">Optional: The name.</param>
        public TestActorRef(ActorSystem system, Props actorProps, IActorRef supervisor = null, string name = null) : base(system, actorProps, supervisor, name)
        {
        }

        public static bool operator ==(TestActorRef<TActor> testActorRef, IActorRef actorRef)
        {
            if(ReferenceEquals(testActorRef, null)) return ReferenceEquals(actorRef, null);
            return testActorRef.Equals(actorRef);
        }

        public static bool operator !=(TestActorRef<TActor> testActorRef, IActorRef actorRef)
        {
            if(ReferenceEquals(testActorRef, null)) return !ReferenceEquals(actorRef, null);
            return !testActorRef.Equals(actorRef);
        }

        public static bool operator ==(IActorRef actorRef, TestActorRef<TActor> testActorRef)
        {
            if(ReferenceEquals(testActorRef, null)) return ReferenceEquals(actorRef, null);
            return testActorRef.Equals(actorRef);
        }

        public static bool operator !=(IActorRef actorRef, TestActorRef<TActor> testActorRef)
        {
            if(ReferenceEquals(testActorRef, null)) return !ReferenceEquals(actorRef, null);
            return !testActorRef.Equals(actorRef);
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