//-----------------------------------------------------------------------
// <copyright file="RunnableGraph.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Streams.Implementation;

namespace Akka.Streams.Dsl
{
    /// <summary>
    /// Flow with attached input and output, can be executed.
    /// </summary>
    public interface IRunnableGraph<TMat> : IGraph<ClosedShape, TMat>
    {
        /// <summary>
        /// Transform only the materialized value of this RunnableGraph, leaving all other properties as they were.
        /// </summary>
        IRunnableGraph<TMat2> MapMaterializedValue<TMat2>(Func<TMat, TMat2> func);

        /// <summary>
        /// Run this flow and return the materialized instance from the flow.
        /// </summary>
        TMat Run(IMaterializer materializer);


        /// <summary>
        /// Change the attributes of this <see cref="IGraph{TShape}"/> to the given ones
        /// and seal the list of attributes. This means that further calls will not be able
        /// to remove these attributes, but instead add new ones. Note that this
        /// operation has no effect on an empty Flow (because the attributes apply
        /// only to the contained processing stages).
        /// </summary>
        new IRunnableGraph<TMat> WithAttributes(Attributes attributes);

        /// <summary>
        /// Add the given attributes to this <see cref="IGraph{TShape}"/>.
        /// Further calls to <see cref="WithAttributes"/>
        /// will not remove these attributes. Note that this
        /// operation has no effect on an empty Flow (because the attributes apply
        /// only to the contained processing stages).
        /// </summary>
        new IRunnableGraph<TMat> AddAttributes(Attributes attributes);

        /// <summary>
        /// Add a name attribute to this Graph.
        /// </summary>
        new IRunnableGraph<TMat> Named(string name);
    }

    public sealed class RunnableGraph<TMat> : IRunnableGraph<TMat>
    {
        public RunnableGraph(IModule module)
        {
            Module = module;
            Shape = ClosedShape.Instance;
        }

        public ClosedShape Shape { get; }

        public IModule Module { get; }

        IGraph<ClosedShape, TMat> IGraph<ClosedShape, TMat>.WithAttributes(Attributes attributes)
            => WithAttributes(attributes);

        public IRunnableGraph<TMat> AddAttributes(Attributes attributes)
            => WithAttributes(Module.Attributes.And(attributes));

        public IRunnableGraph<TMat> Named(string name)
            => AddAttributes(Attributes.CreateName(name));

        public IRunnableGraph<TMat> WithAttributes(Attributes attributes)
            => new RunnableGraph<TMat>(Module.WithAttributes(attributes));

        IGraph<ClosedShape, TMat> IGraph<ClosedShape, TMat>.AddAttributes(Attributes attributes)
            => AddAttributes(attributes);

        IGraph<ClosedShape, TMat> IGraph<ClosedShape, TMat>.Named(string name)
            => Named(name);

        public IGraph<ClosedShape, TMat> Async() => AddAttributes(new Attributes(Attributes.AsyncBoundary.Instance));

        public IRunnableGraph<TMat2> MapMaterializedValue<TMat2>(Func<TMat, TMat2> func)
            => new RunnableGraph<TMat2>(Module.TransformMaterializedValue(func));

        public TMat Run(IMaterializer materializer) => materializer.Materialize(this);
    }

    public static class RunnableGraph
    {
        /// <summary>
        /// A graph with a closed shape is logically a runnable graph, this method makes
        /// it so also in type.
        /// </summary>
        public static RunnableGraph<TMat> FromGraph<TMat>(IGraph<ClosedShape, TMat> g)
            => g as RunnableGraph<TMat> ?? new RunnableGraph<TMat>(g.Module);
    }
}