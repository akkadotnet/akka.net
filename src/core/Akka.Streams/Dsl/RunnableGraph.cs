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

        public IGraph<ClosedShape, TMat> WithAttributes(Attributes attributes)
        {
            return new RunnableGraph<TMat>(Module.WithAttributes(attributes));
        }

        public IGraph<ClosedShape, TMat> AddAttributes(Attributes attributes)
        {
            return WithAttributes(Module.Attributes.And(attributes));
        }

        public IGraph<ClosedShape, TMat> Named(string name)
        {
            return AddAttributes(Attributes.CreateName(name));
        }

        public IGraph<ClosedShape, TMat> Async()
        {
            return AddAttributes(new Attributes(Attributes.AsyncBoundary.Instance));
        }

        public IRunnableGraph<TMat2> MapMaterializedValue<TMat2>(Func<TMat, TMat2> func)
        {
            return new RunnableGraph<TMat2>(Module.TransformMaterializedValue(func));
        }

        public TMat Run(IMaterializer materializer)
        {
            return materializer.Materialize(this);
        }
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