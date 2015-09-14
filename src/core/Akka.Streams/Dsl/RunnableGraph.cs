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
            return new RunnableGraph<TMat>(Module.WithAttributes(attributes).Nest());
        }

        public IGraph<ClosedShape, TMat> Named(string name)
        {
            return WithAttributes(Attributes.CreateName(name));
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
}