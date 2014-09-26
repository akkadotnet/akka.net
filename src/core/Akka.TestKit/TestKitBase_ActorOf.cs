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






        /// <summary>
        /// Create a new <see cref="FSM{TState,TData}"/> as child of the specified supervisor
        /// and returns it as <see cref="TestFSMRef{TActor,TState,TData}"/> to enable inspecting and modifying the FSM directly.
        /// </summary>
        /// <typeparam name="TFsmActor">The type of the actor. It must be a <see cref="FSM{TState,TData}"/></typeparam>
        /// <typeparam name="TState">The type of state name</typeparam>
        /// <typeparam name="TData">The type of state data</typeparam>
        /// <param name="props">The <see cref="Props"/> object</param>
        /// <param name="supervisor">The supervisor</param>
        /// <param name="name">Optional: The name.</param>
        /// <param name="withLogging">Optional: If set to <c>true</c> logs state changes of the FSM as Debug messages. Default is <c>false</c>.</param>
        public TestFSMRef<TFsmActor, TState, TData> ActorOfAsTestFSMRef<TFsmActor, TState, TData>(Props props, ActorRef supervisor, string name = null, bool withLogging = false)
            where TFsmActor : FSM<TState, TData>
        {
            return new TestFSMRef<TFsmActor, TState, TData>(Sys, props, supervisor, name, withLogging);
        }

        /// <summary>
        /// Create a new <see cref="FSM{TState,TData}"/> as child of <see cref="Sys"/>
        /// and returns it as <see cref="TestFSMRef{TActor,TState,TData}"/> to enable inspecting and modifying the FSM directly.
        /// </summary>
        /// <typeparam name="TFsmActor">The type of the actor. It must be a <see cref="FSM{TState,TData}"/> and have a public parameterless constructor</typeparam>
        /// <typeparam name="TState">The type of state name</typeparam>
        /// <typeparam name="TData">The type of state data</typeparam>
        /// <param name="props">The <see cref="Props"/> object</param>
        /// <param name="name">Optional: The name.</param>
        /// <param name="withLogging">Optional: If set to <c>true</c> logs state changes of the FSM as Debug messages. Default is <c>false</c>.</param>
        public TestFSMRef<TFsmActor, TState, TData> ActorOfAsTestFSMRef<TFsmActor, TState, TData>(Props props, string name = null, bool withLogging = false)
            where TFsmActor : FSM<TState, TData>
        {
            return new TestFSMRef<TFsmActor, TState, TData>(Sys, props,NoSupervisor, name, withLogging);
        }


        /// <summary>
        /// Create a new <see cref="FSM{TState,TData}"/> as child of the specified supervisor
        /// and returns it as <see cref="TestFSMRef{TActor,TState,TData}"/> to enable inspecting and modifying the FSM directly.
        /// <typeparamref name="TFsmActor"/> must have a public parameterless constructor.
        /// </summary>
        /// <typeparam name="TFsmActor">The type of the actor. It must have a parameterless public constructor</typeparam>
        /// <typeparam name="TState">The type of state name</typeparam>
        /// <typeparam name="TData">The type of state data</typeparam>
        /// <param name="supervisor">The supervisor</param>
        /// <param name="name">Optional: The name.</param>
        /// <param name="withLogging">Optional: If set to <c>true</c> logs state changes of the FSM as Debug messages. Default is <c>false</c>.</param>
        public TestFSMRef<TFsmActor, TState, TData> ActorOfAsTestFSMRef<TFsmActor, TState, TData>(ActorRef supervisor, string name = null, bool withLogging = false)
            where TFsmActor : FSM<TState, TData>, new()
        {
            return new TestFSMRef<TFsmActor, TState, TData>(Sys,Props.Create<TFsmActor>(), supervisor, name, withLogging);
        }

        /// <summary>
        /// Create a new <see cref="FSM{TState,TData}"/> as child of <see cref="Sys"/>
        /// and returns it as <see cref="TestFSMRef{TActor,TState,TData}"/> to enable inspecting and modifying the FSM directly.
        /// <typeparamref name="TFsmActor"/> must have a public parameterless constructor.
        /// </summary>
        /// <typeparam name="TFsmActor">The type of the actor. It must have a parameterless public constructor</typeparam>
        /// <typeparam name="TState">The type of state name</typeparam>
        /// <typeparam name="TData">The type of state data</typeparam>
        /// <param name="name">Optional: The name.</param>
        /// <param name="withLogging">Optional: If set to <c>true</c> logs state changes of the FSM as Debug messages. Default is <c>false</c>.</param>
        public TestFSMRef<TFsmActor, TState, TData> ActorOfAsTestFSMRef<TFsmActor, TState, TData>(string name = null, bool withLogging = false)
            where TFsmActor : FSM<TState, TData>, new()
        {
            return new TestFSMRef<TFsmActor, TState, TData>(Sys, Props.Create<TFsmActor>(), NoSupervisor, name, withLogging);
        }

        /// <summary>
        /// Create a new <see cref="FSM{TState,TData}"/> as child of the specified supervisor
        /// and returns it as <see cref="TestFSMRef{TActor,TState,TData}"/> to enable inspecting and modifying the FSM directly.
        /// Uses an expression that calls the constructor of <typeparamref name="TFsmActor"/>.
        /// </summary>
        /// <typeparam name="TFsmActor">The type of the actor.</typeparam>
        /// <typeparam name="TState">The type of state name</typeparam>
        /// <typeparam name="TData">The type of state data</typeparam>
        /// <param name="factory">An expression that calls the constructor of <typeparamref name="TFsmActor"/></param>
        /// <param name="supervisor">The supervisor</param>
        /// <param name="name">Optional: The name.</param>
        /// <param name="withLogging">Optional: If set to <c>true</c> logs state changes of the FSM as Debug messages. Default is <c>false</c>.</param>
        public TestFSMRef<TFsmActor, TState, TData> ActorOfAsTestFSMRef<TFsmActor, TState, TData>(Expression<Func<TFsmActor>> factory, ActorRef supervisor, string name = null, bool withLogging = false)
            where TFsmActor : FSM<TState, TData>
        {
            return new TestFSMRef<TFsmActor, TState, TData>(Sys, Props.Create(factory), supervisor, name, withLogging);
        }

        /// <summary>
        /// Create a new <see cref="FSM{TState,TData}"/> as child of <see cref="Sys"/>
        /// and returns it as <see cref="TestFSMRef{TActor,TState,TData}"/> to enable inspecting and modifying the FSM directly.
        /// Uses an expression that calls the constructor of <typeparamref name="TFsmActor"/>.
        /// </summary>
        /// <typeparam name="TFsmActor">The type of the actor.</typeparam>
        /// <typeparam name="TState">The type of state name</typeparam>
        /// <typeparam name="TData">The type of state data</typeparam>
        /// <param name="factory">An expression that calls the constructor of <typeparamref name="TFsmActor"/></param>
        /// <param name="name">Optional: The name.</param>
        /// <param name="withLogging">Optional: If set to <c>true</c> logs state changes of the FSM as Debug messages. Default is <c>false</c>.</param>
        public TestFSMRef<TFsmActor, TState, TData> ActorOfAsTestFSMRef<TFsmActor, TState, TData>(Expression<Func<TFsmActor>> factory, string name = null, bool withLogging = false)
            where TFsmActor : FSM<TState, TData>
        {
            return new TestFSMRef<TFsmActor, TState, TData>(Sys, Props.Create(factory), NoSupervisor, name, withLogging);
        }
    }
}