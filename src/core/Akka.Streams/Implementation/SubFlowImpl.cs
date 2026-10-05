//-----------------------------------------------------------------------
// <copyright file="SubFlowImpl.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Streams.Dsl;

namespace Akka.Streams.Implementation
{
    /// <summary>
    /// Applies a flow to a subflow and merges its resulting streams back into the parent flow.
    /// </summary>
    /// <typeparam name="TIn">The element type entering the flow applied to each substream.</typeparam>
    /// <typeparam name="TMat">The materialized value type of that flow.</typeparam>
    public interface IMergeBack<TIn, TMat>
    {
        /// <summary>
        /// Applies the supplied flow to the substreams and merges the resulting elements back with bounded breadth.
        /// </summary>
        /// <typeparam name="TOut">The output element type of the applied flow.</typeparam>
        /// <param name="flow">The flow applied independently to each substream.</param>
        /// <param name="breadth">The maximum number of substreams processed concurrently.</param>
        /// <returns>A flow of merged substream outputs and the applied flow's materialized value.</returns>
        IFlow<TOut, TMat> Apply<TOut>(Flow<TIn, TOut, TMat> flow, int breadth);
    }

    /// <summary>
    /// Implementation of a subflow that retains its graph and operations for merging or closing substreams.
    /// </summary>
    /// <typeparam name="TIn">The input element type of the retained flow.</typeparam>
    /// <typeparam name="TOut">The output element type of the retained flow.</typeparam>
    /// <typeparam name="TMat">The retained flow's materialized value type.</typeparam>
    /// <typeparam name="TClosed">The result type produced when the subflow is connected to a sink.</typeparam>
    public class SubFlowImpl<TIn, TOut, TMat, TClosed> : SubFlow<TOut, TMat, TClosed>
    {
        private readonly IMergeBack<TIn, TMat> _mergeBackFunction;
        private readonly Func<Sink<TIn, TMat>, TClosed> _finishFunction;

        /// <summary>
        /// Creates a subflow wrapper with operations for merging substreams and finishing with a sink.
        /// </summary>
        /// <param name="flow">The flow graph currently represented by this subflow.</param>
        /// <param name="mergeBackFunction">Applies the flow to substreams and merges results back.</param>
        /// <param name="finishFunction">Connects a sink and produces the closed-subflow result.</param>
        public SubFlowImpl(Flow<TIn, TOut, TMat> flow, IMergeBack<TIn, TMat> mergeBackFunction, Func<Sink<TIn, TMat>, TClosed> finishFunction)
        {
            _mergeBackFunction = mergeBackFunction;
            _finishFunction = finishFunction;
            Flow = flow;
        }

        /// <summary>
        /// Gets the graph currently represented by this subflow.
        /// </summary>
        public Flow<TIn, TOut, TMat> Flow { get; }

        /// <summary>
        /// Appends a flow to each substream while preserving the subflow's merge and finish operations.
        /// </summary>
        /// <typeparam name="T2">The output element type of the appended flow.</typeparam>
        /// <typeparam name="TMat2">The appended flow's materialized value type.</typeparam>
        /// <param name="flow">The flow appended to this subflow.</param>
        /// <returns>A subflow whose elements have type <typeparamref name="T2"/>.</returns>
        public override IFlow<T2, TMat> Via<T2, TMat2>(IGraph<FlowShape<TOut, T2>, TMat2> flow) =>
                new SubFlowImpl<TIn, T2, TMat, TClosed>(Flow.Via(flow), _mergeBackFunction,
                    sink => _finishFunction(sink));

        /// <summary>
        /// Is not implemented by this subflow implementation.
        /// </summary>
        /// <typeparam name="T2">The output element type of the appended flow.</typeparam>
        /// <typeparam name="TMat2">The appended flow's materialized value type.</typeparam>
        /// <typeparam name="TMat3">The combined materialized value type.</typeparam>
        /// <param name="flow">The flow appended to this subflow.</param>
        /// <param name="combine">Combines the original and appended materialized values.</param>
        /// <exception cref="NotImplementedException">This operation is unsupported by this implementation.</exception>
        /// <returns>No value; this implementation always throws.</returns>
        public override IFlow<T2, TMat3> ViaMaterialized<T2, TMat2, TMat3>(IGraph<FlowShape<TOut, T2>, TMat2> flow, Func<TMat, TMat2, TMat3> combine)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Is not implemented by this subflow implementation.
        /// </summary>
        /// <typeparam name="TMat2">The requested mapped materialized value type.</typeparam>
        /// <param name="mapFunc">Maps the flow's materialized value.</param>
        /// <exception cref="NotImplementedException">This operation is unsupported by this implementation.</exception>
        /// <returns>No value; this implementation always throws.</returns>
        public override IFlow<TOut, TMat2> MapMaterializedValue<TMat2>(Func<TMat, TMat2> mapFunc)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Is not implemented by this subflow implementation.
        /// </summary>
        /// <typeparam name="TMat2">The sink's materialized value type.</typeparam>
        /// <param name="sink">The sink to run with the subflow.</param>
        /// <param name="materializer">The materializer that would run the graph.</param>
        /// <exception cref="NotImplementedException">This operation is unsupported by this implementation.</exception>
        /// <returns>No value; this implementation always throws.</returns>
        public override TMat2 RunWith<TMat2>(IGraph<SinkShape<TOut>, TMat2> sink, IMaterializer materializer)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Connects a sink to the subflow and applies the configured finish operation.
        /// </summary>
        /// <typeparam name="TMat2">The sink's materialized value type.</typeparam>
        /// <param name="sink">The sink that closes the subflow.</param>
        /// <returns>The result produced by the finish operation.</returns>
        public override TClosed To<TMat2>(IGraph<SinkShape<TOut>, TMat2> sink) => _finishFunction(Flow.To(sink));

        /// <summary>
        /// Applies the subflow to its substreams and merges the outputs with the given maximum parallelism.
        /// </summary>
        /// <param name="parallelism">The maximum number of substreams processed concurrently.</param>
        /// <returns>A flow whose output merges the processed substreams.</returns>
        public override IFlow<TOut, TMat> MergeSubstreamsWithParallelism(int parallelism) => _mergeBackFunction.Apply(Flow, parallelism);

        /// <summary>
        /// Change the attributes of this <see cref="Flow{TIn,TOut,TMat}"/> to the given ones. Note that this
        /// operation has no effect on an empty Flow (because the attributes apply
        /// only to the contained processing stages).
        /// </summary>
        /// <param name="attributes">The attributes requested for the flow.</param>
        /// <exception cref="NotSupportedException">Attribute changes are not supported by this implementation.</exception>
        /// <returns>No value; this implementation always throws.</returns>
        public SubFlowImpl<TIn, TOut, TMat, TClosed> WithAttributes(Attributes attributes)
        {
            throw new NotSupportedException();
        }
    }
}
