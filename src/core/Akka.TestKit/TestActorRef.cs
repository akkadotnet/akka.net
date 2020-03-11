//-----------------------------------------------------------------------
// <copyright file="TestActorRef.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.TestKit
{
    /// <summary>
    /// This special ActorRef is exclusively for use during unit testing in a single-threaded environment. Therefore, it
    /// overrides the dispatcher to <see cref="CallingThreadDispatcher"/> and sets the receiveTimeout to None. Otherwise,
    /// it acts just like a normal ActorRef. You may retrieve a reference to the underlying actor to test internal logic.
    /// A <see cref="TestActorRef{TActor}"/> can be implicitly casted to an <see cref="IActorRef"/> or you can get the actual
    /// <see cref="IActorRef"/> from the <see cref="TestActorRefBase{TActor}.Ref"/> property.
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

        /// <summary>
        /// Compares a specified <see cref="TestActorRef{TActor}"/> to an <see cref="IActorRef"/> for equality.
        /// </summary>
        /// <param name="testActorRef">The test actor used for comparison</param>
        /// <param name="actorRef">The actor used for comparison</param>
        /// <returns><c>true</c> if both actors are equal; otherwise <c>false</c></returns>
        public static bool operator ==(TestActorRef<TActor> testActorRef, IActorRef actorRef)
        {
            if(ReferenceEquals(testActorRef, null)) return ReferenceEquals(actorRef, null);
            return testActorRef.Equals(actorRef);
        }

        /// <summary>
        /// Compares a specified <see cref="TestActorRef{TActor}"/> to an <see cref="IActorRef"/> for inequality.
        /// </summary>
        /// <param name="testActorRef">The test actor used for comparison</param>
        /// <param name="actorRef">The actor used for comparison</param>
        /// <returns><c>true</c> if both actors are not equal; otherwise <c>false</c></returns>
        public static bool operator !=(TestActorRef<TActor> testActorRef, IActorRef actorRef)
        {
            if(ReferenceEquals(testActorRef, null)) return !ReferenceEquals(actorRef, null);
            return !testActorRef.Equals(actorRef);
        }

        /// <summary>
        /// Compares a specified <see cref="IActorRef"/> to an <see cref="TestActorRef{TActor}"/> for equality.
        /// </summary>
        /// <param name="actorRef">The actor used for comparison</param>
        /// <param name="testActorRef">The test actor used for comparison</param>
        /// <returns><c>true</c> if both actors are equal; otherwise <c>false</c></returns>
        public static bool operator ==(IActorRef actorRef, TestActorRef<TActor> testActorRef)
        {
            if(ReferenceEquals(testActorRef, null)) return ReferenceEquals(actorRef, null);
            return testActorRef.Equals(actorRef);
        }

        /// <summary>
        /// Compares a specified <see cref="IActorRef"/> to an <see cref="TestActorRef{TActor}"/> for inequality.
        /// </summary>
        /// <param name="actorRef">The actor used for comparison</param>
        /// <param name="testActorRef">The test actor used for comparison</param>
        /// <returns><c>true</c> if both actors are not equal; otherwise <c>false</c></returns>
        public static bool operator !=(IActorRef actorRef, TestActorRef<TActor> testActorRef)
        {
            if(ReferenceEquals(testActorRef, null)) return !ReferenceEquals(actorRef, null);
            return !testActorRef.Equals(actorRef);
        }
    }
}
