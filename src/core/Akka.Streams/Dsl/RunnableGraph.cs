//-----------------------------------------------------------------------
// <copyright file="RunnableGraph.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Streams.Implementation;

namespace Akka.Streams.Dsl
{
    /// <summary>
    /// Flow with attached input and output, can be executed.
    /// </summary>
    /// <typeparam name="TMat">TBD</typeparam>
    public interface IRunnableGraph<out TMat> : IGraph<ClosedShape, TMat>
    {
        /// <summary>
        /// Transform only the materialized value of this RunnableGraph, leaving all other properties as they were.
        /// </summary>
        /// <typeparam name="TMat2">TBD</typeparam>
        /// <param name="func">TBD</param>
        /// <returns>TBD</returns>
        IRunnableGraph<TMat2> MapMaterializedValue<TMat2>(Func<TMat, TMat2> func);

        /// <summary>
        /// Run this flow and return the materialized instance from the flow.
        /// </summary>
        /// <param name="materializer">TBD</param>
        /// <returns>TBD</returns>
        TMat Run(IMaterializer materializer);


        /// <summary>
        /// Change the attributes of this <see cref="IGraph{TShape}"/> to the given ones
        /// and seal the list of attributes. This means that further calls will not be able
        /// to remove these attributes, but instead add new ones. Note that this
        /// operation has no effect on an empty Flow (because the attributes apply
        /// only to the contained processing stages).
        /// </summary>
        /// <param name="attributes">TBD</param>
        /// <returns>TBD</returns>
        new IRunnableGraph<TMat> WithAttributes(Attributes attributes);

        /// <summary>
        /// Add the given attributes to this <see cref="IGraph{TShape}"/>.
        /// Further calls to <see cref="WithAttributes"/>
        /// will not remove these attributes. Note that this
        /// operation has no effect on an empty Flow (because the attributes apply
        /// only to the contained processing stages).
        /// </summary>
        /// <param name="attributes">TBD</param>
        /// <returns>TBD</returns>
        new IRunnableGraph<TMat> AddAttributes(Attributes attributes);

        /// <summary>
        /// Add a name attribute to this Graph.
        /// </summary>
        /// <param name="name">TBD</param>
        /// <returns>TBD</returns>
        new IRunnableGraph<TMat> Named(string name);
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="TMat">TBD</typeparam>
    public sealed class RunnableGraph<TMat> : IRunnableGraph<TMat>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="module">TBD</param>
        public RunnableGraph(IModule module)
        {
            Module = module;
            Shape = ClosedShape.Instance;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public ClosedShape Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public IModule Module { get; }

        IGraph<ClosedShape, TMat> IGraph<ClosedShape, TMat>.WithAttributes(Attributes attributes)
            => WithAttributes(attributes);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="attributes">TBD</param>
        /// <returns>TBD</returns>
        public IRunnableGraph<TMat> AddAttributes(Attributes attributes)
            => WithAttributes(Module.Attributes.And(attributes));

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        /// <returns>TBD</returns>
        public IRunnableGraph<TMat> Named(string name)
            => AddAttributes(Attributes.CreateName(name));

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public IRunnableGraph<TMat> Async()
           => AddAttributes(new Attributes(Attributes.AsyncBoundary.Instance));

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="attributes">TBD</param>
        /// <returns>TBD</returns>
        public IRunnableGraph<TMat> WithAttributes(Attributes attributes)
            => new RunnableGraph<TMat>(Module.WithAttributes(attributes));

        IGraph<ClosedShape, TMat> IGraph<ClosedShape, TMat>.AddAttributes(Attributes attributes)
            => AddAttributes(attributes);

        IGraph<ClosedShape, TMat> IGraph<ClosedShape, TMat>.Named(string name)
            => Named(name);

        IGraph<ClosedShape, TMat> IGraph<ClosedShape, TMat>.Async() => Async();

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TMat2">TBD</typeparam>
        /// <param name="func">TBD</param>
        /// <returns>TBD</returns>
        public IRunnableGraph<TMat2> MapMaterializedValue<TMat2>(Func<TMat, TMat2> func)
            => new RunnableGraph<TMat2>(Module.TransformMaterializedValue(func));

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="materializer">TBD</param>
        /// <returns>TBD</returns>
        public TMat Run(IMaterializer materializer) => materializer.Materialize(this);
    }

    /// <summary>
    /// TBD
    /// </summary>
    public static class RunnableGraph
    {
        /// <summary>
        /// A graph with a closed shape is logically a runnable graph, this method makes
        /// it so also in type.
        /// </summary>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="g">TBD</param>
        /// <returns>TBD</returns>
        public static RunnableGraph<TMat> FromGraph<TMat>(IGraph<ClosedShape, TMat> g)
            => g as RunnableGraph<TMat> ?? new RunnableGraph<TMat>(g.Module);
    }
}
