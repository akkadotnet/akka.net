//-----------------------------------------------------------------------
// <copyright file="TestKitBase_ActorOf.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq.Expressions;
using Akka.Actor;
using Akka.Actor.Dsl;

namespace Akka.TestKit
{
    /// <summary>
    /// TBD
    /// </summary>
    public partial class TestKitBase
    {
        private const IActorRef NoSupervisor = null;

        /// <summary>
        /// Create a new actor as child of <see cref="Sys" />.
        /// </summary>
        /// <param name="props">The props configuration object</param>
        /// <returns>TBD</returns>
        public IActorRef ActorOf(Props props)
        {
            return Sys.ActorOf(props, null);
        }

        /// <summary>
        /// Create a new actor as child of <see cref="Sys" />.
        /// </summary>
        /// <param name="props">The props configuration object</param>
        /// <param name="name">The name of the actor.</param>
        /// <returns>TBD</returns>
        public IActorRef ActorOf(Props props, string name)
        {
            return Sys.ActorOf(props, name);
        }

        /// <summary>
        /// Create a new actor as child of <see cref="Sys" />.
        /// </summary>
        /// <typeparam name="TActor">The type of the actor. It must have a parameterless public constructor</typeparam>
        /// <returns>TBD</returns>
        public IActorRef ActorOf<TActor>() where TActor : ActorBase, new()
        {
            return Sys.ActorOf(Props.Create<TActor>(), null);
        }

        /// <summary>
        /// Create a new actor as child of <see cref="Sys" />.
        /// </summary>
        /// <typeparam name="TActor">The type of the actor. It must have a parameterless public constructor</typeparam>
        /// <param name="name">The name of the actor.</param>
        /// <returns>TBD</returns>
        public IActorRef ActorOf<TActor>(string name) where TActor : ActorBase, new()
        {
            return Sys.ActorOf(Props.Create<TActor>(), name);
        }

        /// <summary>
        /// Create a new actor as child of <see cref="Sys" /> using an expression that calls the constructor
        /// of <typeparamref name="TActor"/>.
        /// <example>
        /// <code>ActorOf&lt;MyActor&gt;(()=>new MyActor("value", 4711))</code>
        /// </example>
        /// </summary>
        /// <typeparam name="TActor">The type of the actor.</typeparam>
        /// <param name="factory">An expression that calls the constructor of <typeparamref name="TActor"/></param>
        /// <returns>TBD</returns>
        public IActorRef ActorOf<TActor>(Expression<Func<TActor>> factory) where TActor : ActorBase
        {
            return Sys.ActorOf(Props.Create(factory), null);
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
        /// <param name="name">The name of the actor.</param>
        /// <returns>TBD</returns>
        public IActorRef ActorOf<TActor>(Expression<Func<TActor>> factory, string name) where TActor : ActorBase
        {
            return Sys.ActorOf(Props.Create(factory), name);
        }

        /// <summary>
        /// Creates a new actor by defining the behavior inside the <paramref name="configure"/> action.
        /// <example>
        /// <code>
        /// ActorOf(c =>
        /// {
        ///     c.Receive&lt;string&gt;((msg, ctx) => ctx.Sender.Tell("Hello " + msg));
        /// });
        /// </code>
        /// </example>
        /// </summary>
        /// <param name="configure">An action that configures the actor's behavior.</param>
        /// <param name="name">Optional: The name of the actor.</param>
        /// <returns>TBD</returns>
        public IActorRef ActorOf(Action<IActorDsl, IActorContext> configure, string name = null)
        {
            return ActExtensions.ActorOf(this, configure, name);
        }

        /// <summary>
        /// Creates a new actor by defining the behavior inside the <paramref name="configure"/> action.
        /// <example>
        /// <code>
        /// ActorOf(c =>
        /// {
        ///     c.Receive&lt;string&gt;((msg, ctx) => ctx.Sender.Tell("Hello " + msg));
        /// });
        /// </code>
        /// </example>
        /// </summary>
        /// <param name="configure">An action that configures the actor's behavior.</param>
        /// <param name="name">Optional: The name of the actor.</param>
        /// <returns>TBD</returns>
        public IActorRef ActorOf(Action<IActorDsl> configure, string name = null)
        {
            return ActExtensions.ActorOf(this, configure, name);
        }

        /// <summary>
        /// Creates an <see cref="ActorSelection(Akka.Actor.ActorPath)"/>
        /// </summary>
        /// <param name="actorPath">The path of the actor(s) we want to select.</param>
        /// <returns>An ActorSelection</returns>
        public ActorSelection ActorSelection(ActorPath actorPath)
        {
            return Sys.ActorSelection(actorPath);
        }

        ///<summary>
        /// Creates an <see cref="ActorSelection(string)"/>
        /// </summary>
        /// <param name="actorPath">The path of the actor(s) we want to select.</param>
        /// <returns>An ActorSelection</returns>
        public ActorSelection ActorSelection(string actorPath)
        {
            return Sys.ActorSelection(actorPath);
        }

        /// <summary>
        /// Creates an <see cref="ActorSelection(string)"/>
        /// </summary>
        /// <param name="anchorRef">The base actor that anchors the <paramref name="actorPath"/>.</param>
        /// <param name="actorPath">The path of the actor(s) we want to select.</param>
        /// <returns>An ActorSelection</returns>
        public ActorSelection ActorSelection(IActorRef anchorRef, string actorPath)
        {
            return Sys.ActorSelection(anchorRef, actorPath);
        }

        /// <summary>
        /// Create a new actor as child of specified supervisor and returns it as <see cref="TestActorRef{TActor}"/>
        /// to enable access to the underlying actor instance via <see cref="TestActorRefBase{TActor}.UnderlyingActor"/>.
        /// </summary>
        /// <typeparam name="TActor">The type of the actor. It must have a parameterless public constructor</typeparam>
        /// <param name="props">The <see cref="Props"/> object</param>
        /// <param name="supervisor">The supervisor</param>
        /// <param name="name">Optional: The name.</param>
        /// <returns>TBD</returns>
        public TestActorRef<TActor> ActorOfAsTestActorRef<TActor>(Props props, IActorRef supervisor, string name = null) where TActor : ActorBase
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
        /// <returns>TBD</returns>
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
        /// <returns>TBD</returns>
        public TestActorRef<TActor> ActorOfAsTestActorRef<TActor>(Expression<Func<TActor>> factory, IActorRef supervisor, string name = null) where TActor : ActorBase
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
        /// <returns>TBD</returns>
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
        /// <returns>TBD</returns>
        public TestActorRef<TActor> ActorOfAsTestActorRef<TActor>(IActorRef supervisor, string name = null) where TActor : ActorBase, new()
        {
            return new TestActorRef<TActor>(Sys, Props.Create<TActor>(), supervisor, name);
        }

        /// <summary>
        /// Create a new actor as child of <see cref="Sys"/> and returns it as <see cref="TestActorRef{TActor}"/> 
        /// to enable access to the underlying actor instance via <see cref="TestActorRefBase{TActor}.UnderlyingActor"/>.
        /// </summary>
        /// <typeparam name="TActor">The type of the actor. It must have a parameterless public constructor</typeparam>
        /// <param name="name">Optional: The name.</param>
        /// <returns>TBD</returns>
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
        /// <returns>TBD</returns>
        public TestFSMRef<TFsmActor, TState, TData> ActorOfAsTestFSMRef<TFsmActor, TState, TData>(Props props, IActorRef supervisor, string name = null, bool withLogging = false)
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
        /// <returns>TBD</returns>
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
        /// <returns>TBD</returns>
        public TestFSMRef<TFsmActor, TState, TData> ActorOfAsTestFSMRef<TFsmActor, TState, TData>(IActorRef supervisor, string name = null, bool withLogging = false)
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
        /// <returns>TBD</returns>
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
        /// <returns>TBD</returns>
        public TestFSMRef<TFsmActor, TState, TData> ActorOfAsTestFSMRef<TFsmActor, TState, TData>(Expression<Func<TFsmActor>> factory, IActorRef supervisor, string name = null, bool withLogging = false)
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
        /// <returns>TBD</returns>
        public TestFSMRef<TFsmActor, TState, TData> ActorOfAsTestFSMRef<TFsmActor, TState, TData>(Expression<Func<TFsmActor>> factory, string name = null, bool withLogging = false)
            where TFsmActor : FSM<TState, TData>
        {
            return new TestFSMRef<TFsmActor, TState, TData>(Sys, Props.Create(factory), NoSupervisor, name, withLogging);
        }
    }
}
