//-----------------------------------------------------------------------
// <copyright file="SubFlow.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Reactive.Streams;

namespace Akka.Streams.Dsl
{
    /// <summary>
    /// A "stream of streams" sub-flow of data elements, e.g. produced by <see cref="Akka.Streams.Implementation.Fusing.GroupBy{T,TKey}"/>.
    /// SubFlows cannot contribute to the super-flowâ€™s materialized value since they
    /// are materialized later, during the runtime of the flow graph processing.
    /// </summary>
    /// <typeparam name="TOut">The element type emitted by each subflow.</typeparam>
    /// <typeparam name="TMat">The materialized value type preserved from the enclosing stream.</typeparam>
    /// <typeparam name="TClosed">The type returned when the subflow is connected to a sink.</typeparam>
    public abstract class SubFlow<TOut, TMat, TClosed> : IFlow<TOut, TMat>
    {
        /// <summary>
        /// Connects a flow stage to each subflow while preserving the enclosing stream's materialized value.
        /// </summary>
        /// <typeparam name="T2">The output element type of the connected flow stage.</typeparam>
        /// <typeparam name="TMat2">The connected flow stage's materialized value type, which is not retained by this overload.</typeparam>
        /// <param name="flow">The flow stage applied to the output of each subflow.</param>
        public abstract IFlow<T2, TMat> Via<T2, TMat2>(IGraph<FlowShape<TOut, T2>, TMat2> flow);

        /// <summary>
        /// Connects a flow stage to each subflow and combines its materialized value with the enclosing stream's value.
        /// </summary>
        /// <typeparam name="T2">The output element type of the connected flow stage.</typeparam>
        /// <typeparam name="TMat2">The connected flow stage's materialized value type.</typeparam>
        /// <typeparam name="TMat3">The value type returned by <paramref name="combine"/>.</typeparam>
        /// <param name="flow">The flow stage applied to each subflow.</param>
        /// <param name="combine">Combines the enclosing stream's and connected flow's materialized values, in that order.</param>
        /// <remarks>The current built-in implementation throws <see cref="NotImplementedException"/> when this method is called.</remarks>
        public abstract IFlow<T2, TMat3> ViaMaterialized<T2, TMat2, TMat3>(IGraph<FlowShape<TOut, T2>, TMat2> flow, Func<TMat, TMat2, TMat3> combine);

        /// <summary>
        /// Maps the materialized value associated with this subflow.
        /// </summary>
        /// <typeparam name="TMat2">The mapped materialized value type.</typeparam>
        /// <param name="mapFunc">Maps the enclosing stream's materialized value to a new value.</param>
        /// <remarks>The current built-in implementation throws <see cref="NotImplementedException"/> when this method is called.</remarks>
        public abstract IFlow<TOut, TMat2> MapMaterializedValue<TMat2>(Func<TMat, TMat2> mapFunc);

        /// <summary>
        /// Connect this <see cref="Source{TOut,TMat}"/> to a <see cref="Sink{TIn,TMat}"/> and run it. The returned value is the materialized value
        /// of the <see cref="Sink{TIn,TMat}"/>, e.g. the <see cref="IPublisher{TIn}"/> of a <see cref="Sink.Publisher{TIn}"/>.
        /// </summary>
        /// <typeparam name="TMat2">The sink materialized value type returned by this method.</typeparam>
        /// <param name="sink">The sink attached to each subflow.</param>
        /// <param name="materializer">The materializer used to run the connected subflows.</param>
        /// <remarks>The current built-in implementation throws <see cref="NotImplementedException"/> when this method is called.</remarks>
        public abstract TMat2 RunWith<TMat2>(IGraph<SinkShape<TOut>, TMat2> sink, IMaterializer materializer);

        /// <summary>
        /// Attach a <see cref="Sink"/> to each sub-flow, closing the overall Graph that is being
        /// constructed.
        /// </summary>
        /// <summary>
        /// Attaches a sink to every subflow and closes the overall graph being constructed.
        /// </summary>
        /// <typeparam name="TMat2">The sink materialized value type.</typeparam>
        /// <param name="sink">The sink connected to each subflow.</param>
        public abstract TClosed To<TMat2>(IGraph<SinkShape<TOut>, TMat2> sink);

        /// <summary>
        /// Flatten the sub-flows back into the super-flow by performing a merge
        /// without parallelism limit (i.e. having an unbounded number of sub-flows
        /// active concurrently).
        /// </summary>
        public virtual IFlow<TOut, TMat> MergeSubstreams() => MergeSubstreamsWithParallelism(int.MaxValue);

        /// <summary>
        /// Flatten the sub-flows back into the super-flow by performing a merge
        /// with the given parallelism limit. This means that only up to <paramref name="parallelism"/>
        /// substreams will be executed at any given time. Substreams that are not
        /// yet executed are also not materialized, meaning that back-pressure will
        /// be exerted at the operator that creates the substreams when the parallelism
        /// limit is reached.
        /// </summary>
        /// <param name="parallelism">The maximum number of subflows materialized and run concurrently.</param>
        public abstract IFlow<TOut, TMat> MergeSubstreamsWithParallelism(int parallelism);

        /// <summary>
        /// Flatten the sub-flows back into the super-flow by concatenating them.
        /// This is usually a bad idea when combined with <see cref="Akka.Streams.Implementation.Fusing.GroupBy{TIn,TKey}"/>
        /// since it can easily lead to deadlockâ€”the concatenation does not consume from the second
        /// substream until the first has finished and the <see cref="Akka.Streams.Implementation.Fusing.GroupBy{TIn,TKey}"/>
        /// stage will get back-pressure from the second stream.
        /// </summary>
        public virtual IFlow<TOut, TMat> ConcatSubstream() => MergeSubstreamsWithParallelism(1);
    }
}
