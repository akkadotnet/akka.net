//-----------------------------------------------------------------------
// <copyright file="RunnableGraph.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Streams.Implementation;

namespace Akka.Streams.Dsl
{
    /// <summary>
    /// Flow with attached input and output, can be executed.
    /// </summary>
    /// <typeparam name="TMat">The type of value returned when the graph is materialized.</typeparam>
    public interface IRunnableGraph<out TMat> : IGraph<ClosedShape, TMat>
    {
        /// <summary>
        /// Transform only the materialized value of this RunnableGraph, leaving all other properties as they were.
        /// </summary>
        /// <typeparam name="TMat2">The replacement materialized value type.</typeparam>
        /// <param name="func">Maps the graph's materialized value to the replacement value.</param>
        /// <returns>A runnable graph with the same graph structure and the mapped materialized value.</returns>
        IRunnableGraph<TMat2> MapMaterializedValue<TMat2>(Func<TMat, TMat2> func);

        /// <summary>
        /// Run this flow and return the materialized instance from the flow.
        /// </summary>
        /// <param name="materializer">The materializer used to run the graph.</param>
        /// <returns>The value produced when this graph is materialized.</returns>
        TMat Run(IMaterializer materializer);
        
        /// <summary>
        /// Run this flow and return the materialized instance from the flow.
        /// </summary>
        /// <param name="actorSystem">The actor system whose materializer runs the graph.</param>
        /// <returns>The value produced when this graph is materialized.</returns>
        TMat Run(ActorSystem actorSystem);


        /// <summary>
        /// Change the attributes of this <see cref="IGraph{TShape}"/> to the given ones
        /// and seal the list of attributes. This means that further calls will not be able
        /// to remove these attributes, but instead add new ones. Note that this
        /// operation has no effect on an empty Flow (because the attributes apply
        /// only to the contained processing stages).
        /// </summary>
        /// <param name="attributes">The attributes to apply to the graph's stages.</param>
        /// <returns>This runnable graph with the supplied attributes.</returns>
        new IRunnableGraph<TMat> WithAttributes(Attributes attributes);

        /// <summary>
        /// Add the given attributes to this <see cref="IGraph{TShape}"/>.
        /// Further calls to <see cref="WithAttributes"/>
        /// will not remove these attributes. Note that this
        /// operation has no effect on an empty Flow (because the attributes apply
        /// only to the contained processing stages).
        /// </summary>
        /// <param name="attributes">The attributes to add to those already attached to the graph.</param>
        /// <returns>This runnable graph with the added attributes.</returns>
        new IRunnableGraph<TMat> AddAttributes(Attributes attributes);

        /// <summary>
        /// Add a name attribute to this Graph.
        /// </summary>
        /// <param name="name">The graph name.</param>
        /// <returns>This runnable graph with the name attribute.</returns>
        new IRunnableGraph<TMat> Named(string name);
    }

    /// <summary>
    /// A closed stream graph that can be materialized and run.
    /// </summary>
    /// <typeparam name="TMat">The type of materialized value.</typeparam>
    public sealed class RunnableGraph<TMat> : IRunnableGraph<TMat>
    {
        /// <summary>
        /// Creates a runnable graph from its graph module.
        /// </summary>
        /// <param name="module">The module containing the graph and its materialized value.</param>
        public RunnableGraph(IModule module)
        {
            Module = module;
            Shape = ClosedShape.Instance;
        }

        /// <summary>
        /// The closed shape of this runnable graph.
        /// </summary>
        public ClosedShape Shape { get; }

        /// <summary>
        /// The module containing this graph's stages and materialized value.
        /// </summary>
        public IModule Module { get; }

        IGraph<ClosedShape, TMat> IGraph<ClosedShape, TMat>.WithAttributes(Attributes attributes)
            => WithAttributes(attributes);

        /// <summary>
        /// Adds attributes to those already attached to this graph.
        /// </summary>
        /// <param name="attributes">The attributes to add.</param>
        /// <returns>This runnable graph with the added attributes.</returns>
        public IRunnableGraph<TMat> AddAttributes(Attributes attributes)
            => WithAttributes(Module.Attributes.And(attributes));

        /// <summary>
        /// Adds a name attribute to this graph.
        /// </summary>
        /// <param name="name">The graph name.</param>
        /// <returns>This runnable graph with the name attribute.</returns>
        public IRunnableGraph<TMat> Named(string name)
            => AddAttributes(Attributes.CreateName(name));

        /// <summary>
        /// Adds an asynchronous boundary to this graph.
        /// </summary>
        /// <returns>This runnable graph with an async-boundary attribute.</returns>
        public IRunnableGraph<TMat> Async()
           => AddAttributes(new Attributes(Attributes.AsyncBoundary.Instance));

        /// <summary>
        /// Replaces the attributes attached to this graph module.
        /// </summary>
        /// <param name="attributes">The attributes to apply.</param>
        /// <returns>A runnable graph with the supplied attributes.</returns>
        public IRunnableGraph<TMat> WithAttributes(Attributes attributes)
            => new RunnableGraph<TMat>(Module.WithAttributes(attributes));

        IGraph<ClosedShape, TMat> IGraph<ClosedShape, TMat>.AddAttributes(Attributes attributes)
            => AddAttributes(attributes);

        IGraph<ClosedShape, TMat> IGraph<ClosedShape, TMat>.Named(string name)
            => Named(name);

        IGraph<ClosedShape, TMat> IGraph<ClosedShape, TMat>.Async() => Async();

        /// <summary>
        /// Transforms this graph's materialized value, leaving its graph structure unchanged.
        /// </summary>
        /// <typeparam name="TMat2">The replacement materialized value type.</typeparam>
        /// <param name="func">Maps this graph's materialized value to the replacement value.</param>
        /// <returns>A runnable graph with the mapped materialized value.</returns>
        public IRunnableGraph<TMat2> MapMaterializedValue<TMat2>(Func<TMat, TMat2> func)
            => new RunnableGraph<TMat2>(Module.TransformMaterializedValue(func));

        /// <summary>
        /// Compiles the graph and executes it, returning the materialized value of the flow.
        /// </summary>
        /// <param name="materializer">A materializer instance.</param>
        /// <returns>The materialized value.</returns>
        public TMat Run(IMaterializer materializer) => materializer.Materialize(this);

        /// <summary>
        /// Compiles the graph and executes it, returning the materialized value of the flow.
        /// </summary>
        /// <param name="actorSystem">The <see cref="ActorSystem"/>.</param>
        /// <returns>The materialized value.</returns>
        public TMat Run(ActorSystem actorSystem) =>
            actorSystem.Materializer().Materialize(this);
    }

    /// <summary>
    /// Factory methods for runnable graphs.
    /// </summary>
    public static class RunnableGraph
    {
        /// <summary>
        /// A graph with a closed shape is logically a runnable graph, this method makes
        /// it so also in type.
        /// </summary>
        /// <typeparam name="TMat">The graph's materialized value type.</typeparam>
        /// <param name="g">The graph with a closed shape to wrap.</param>
        /// <returns>The graph represented as a <see cref="RunnableGraph{TMat}"/>.</returns>
        public static RunnableGraph<TMat> FromGraph<TMat>(IGraph<ClosedShape, TMat> g)
            => g as RunnableGraph<TMat> ?? new RunnableGraph<TMat>(g.Module);
    }
}
