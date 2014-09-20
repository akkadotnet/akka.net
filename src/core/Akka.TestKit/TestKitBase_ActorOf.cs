using System;
using System.Linq.Expressions;
using Akka.Actor;

namespace Akka.TestKit
{
    public partial class TestKitBase
    {
        private const ActorRef NoSupervisor = null;

        public ActorRef ActorOf(Props props, string name = null)
        {
            return Sys.ActorOf(props, name);
        }

        /// <summary>
        /// Create a new actor as child of <see cref="Sys" />.
        /// </summary>
        /// <typeparam name="TActor">The type of the actor. It must have a parameterless public constructor</typeparam>
        /// <param name="name">The name.</param>
        public ActorRef ActorOf<TActor>(string name = null) where TActor : ActorBase, new()
        {
            return Sys.ActorOf(Props.Create<TActor>(), name);
        }

        /// <summary>
        /// Create a new actor as child of <see cref="Sys" /> using an expression that calls the constructor
        /// of <typeparamref name="TActor"/>.
        /// <example>
        /// <code>ActorOf&lt;MyActor&gt;(()=>new MyActor("value", 4711), "test-actor")</code>
        /// </example>
        /// </summary>
        /// <typeparam name="TActor">The type of the actor.</typeparam>
        /// <param name="factory">An expression that calls the constructor of <typeparamref name="TActor"/></param>
        /// <param name="name">The name.</param>
        public ActorRef ActorOf<TActor>(Expression<Func<TActor>> factory, string name = null) where TActor : ActorBase
        {
            return Sys.ActorOf(Props.Create(factory), name);
        }

        public ActorSelection ActorSelection(ActorPath actorPath)
        {
            return Sys.ActorSelection(actorPath);
        }

        public ActorSelection ActorSelection(string actorPath)
        {
            return Sys.ActorSelection(actorPath);
        }


        /// <summary>
        /// Create a new actor as child of specified supervisor and returns it as <see cref="TestActorRef{TActor}"/>
        /// to enable access to the underlying actor instance via <see cref="TestActorRefBase{TActor}.UnderlyingActor"/>.
        /// </summary>
        /// <typeparam name="TActor">The type of the actor. It must have a parameterless public constructor</typeparam>
        /// <param name="props">The <see cref="Props"/> object</param>
        /// <param name="supervisor">The supervisor</param>
        /// <param name="name">Optional: The name.</param>
        public TestActorRef<TActor> ActorOfAsTestActorRef<TActor>(Props props, ActorRef supervisor, string name = null) where TActor : ActorBase
        {
            return new TestActorRef<TActor>(Sys, props, supervisor, name);
        }

        /// <summary>
        /// Create a new actor as child of <see cref="Sys"/> and returns it as <see cref="TestActorRef{TActor}"/> 
        /// to enable access to the underlying actor instance via <see cref="TestActorRefBase{TActor}.UnderlyingActor"/>.
        /// </summary>
        /// <typeparam name="TActor">The type of the actor. It must have a parameterless public constructor</typeparam>
        /// <param name="props">The <see cref="Props"/> object</param>
        /// <param name="name">Optional: The name.</param>
        public TestActorRef<TActor> ActorOfAsTestActorRef<TActor>(Props props, string name = null) where TActor : ActorBase
        {
            return new TestActorRef<TActor>(Sys, props, NoSupervisor, name);
        }

        /// <summary>
        /// Create a new actor as child of the specified supervisor and returns it as <see cref="TestActorRef{TActor}"/> 
        /// to enable access to the underlying actor instance via <see cref="TestActorRefBase{TActor}.UnderlyingActor"/>.
        /// Uses an expression that calls the constructor of <typeparamref name="TActor"/>.
        /// <example>
        /// <code>ActorOf&lt;MyActor&gt;(()=>new MyActor("value", 4711), "test-actor")</code>
        /// </example>
        /// </summary>
        /// <typeparam name="TActor">The type of the actor.</typeparam>
        /// <param name="factory">An expression that calls the constructor of <typeparamref name="TActor"/></param>
        /// <param name="supervisor">The supervisor</param>
        /// <param name="name">Optional: The name.</param>
        public TestActorRef<TActor> ActorOfAsTestActorRef<TActor>(Expression<Func<TActor>> factory, ActorRef supervisor, string name = null) where TActor : ActorBase
        {
            return new TestActorRef<TActor>(Sys, Props.Create(factory), supervisor, name);
        }

        /// <summary>
        /// Create a new actor as child of <see cref="Sys"/> and returns it as <see cref="TestActorRef{TActor}"/> 
        /// to enable access to the underlying actor instance via <see cref="TestActorRefBase{TActor}.UnderlyingActor"/>.
        /// Uses an expression that calls the constructor of <typeparamref name="TActor"/>.
        /// <example>
        /// <code>ActorOf&lt;MyActor&gt;(()=>new MyActor("value", 4711), "test-actor")</code>
        /// </example>
        /// </summary>
        /// <typeparam name="TActor">The type of the actor.</typeparam>
        /// <param name="factory">An expression that calls the constructor of <typeparamref name="TActor"/></param>
        /// <param name="name">Optional: The name.</param>
        public TestActorRef<TActor> ActorOfAsTestActorRef<TActor>(Expression<Func<TActor>> factory, string name = null) where TActor : ActorBase
        {
            return new TestActorRef<TActor>(Sys, Props.Create(factory), NoSupervisor, name);
        }

        /// <summary>
        /// Create a new actor as child of the specified supervisor and returns it as <see cref="TestActorRef{TActor}"/> 
        /// to enable access to the underlying actor instance via <see cref="TestActorRefBase{TActor}.UnderlyingActor"/>.
        /// </summary>
        /// <typeparam name="TActor">The type of the actor. It must have a parameterless public constructor</typeparam>
        /// <param name="supervisor">The supervisor</param>
        /// <param name="name">Optional: The name.</param>
        public TestActorRef<TActor> ActorOfAsTestActorRef<TActor>(ActorRef supervisor, string name = null) where TActor : ActorBase, new()
        {
            return new TestActorRef<TActor>(Sys, Props.Create<TActor>(), supervisor, name);
        }

        /// <summary>
        /// Create a new actor as child of <see cref="Sys"/> and returns it as <see cref="TestActorRef{TActor}"/> 
        /// to enable access to the underlying actor instance via <see cref="TestActorRefBase{TActor}.UnderlyingActor"/>.
        /// </summary>
        /// <typeparam name="TActor">The type of the actor. It must have a parameterless public constructor</typeparam>
        /// <param name="name">Optional: The name.</param>
        public TestActorRef<TActor> ActorOfAsTestActorRef<TActor>(string name = null) where TActor : ActorBase, new()
        {
            return new TestActorRef<TActor>(Sys, Props.Create<TActor>(), NoSupervisor, name);
        }

    }
}