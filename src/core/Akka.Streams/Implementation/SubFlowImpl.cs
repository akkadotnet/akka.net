//-----------------------------------------------------------------------
// <copyright file="SubFlowImpl.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Streams.Dsl;

namespace Akka.Streams.Implementation
{
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="TMat">TBD</typeparam>
    public interface IMergeBack<TIn, TMat>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="breadth">TBD</param>
        /// <returns>TBD</returns>
        IFlow<TOut, TMat> Apply<TOut>(Flow<TIn, TOut, TMat> flow, int breadth);
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    /// <typeparam name="TMat">TBD</typeparam>
    /// <typeparam name="TClosed">TBD</typeparam>
    public class SubFlowImpl<TIn, TOut, TMat, TClosed> : SubFlow<TOut, TMat, TClosed>
    {
        private readonly IMergeBack<TIn, TMat> _mergeBackFunction;
        private readonly Func<Sink<TIn, TMat>, TClosed> _finishFunction;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="flow">TBD</param>
        /// <param name="mergeBackFunction">TBD</param>
        /// <param name="finishFunction">TBD</param>
        public SubFlowImpl(Flow<TIn, TOut, TMat> flow, IMergeBack<TIn, TMat> mergeBackFunction, Func<Sink<TIn, TMat>, TClosed> finishFunction)
        {
            _mergeBackFunction = mergeBackFunction;
            _finishFunction = finishFunction;
            Flow = flow;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public Flow<TIn, TOut, TMat> Flow { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T2">TBD</typeparam>
        /// <typeparam name="TMat2">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <returns>TBD</returns>
        public override IFlow<T2, TMat> Via<T2, TMat2>(IGraph<FlowShape<TOut, T2>, TMat2> flow) =>
                new SubFlowImpl<TIn, T2, TMat, TClosed>(Flow.Via(flow), _mergeBackFunction,
                    sink => _finishFunction(sink));

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T2">TBD</typeparam>
        /// <typeparam name="TMat2">TBD</typeparam>
        /// <typeparam name="TMat3">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="combine">TBD</param>
        /// <exception cref="NotImplementedException">TBD</exception>
        /// <returns>TBD</returns>
        public override IFlow<T2, TMat3> ViaMaterialized<T2, TMat2, TMat3>(IGraph<FlowShape<TOut, T2>, TMat2> flow, Func<TMat, TMat2, TMat3> combine)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TMat2">TBD</typeparam>
        /// <param name="mapFunc">TBD</param>
        /// <exception cref="NotImplementedException">TBD</exception>
        /// <returns>TBD</returns>
        public override IFlow<TOut, TMat2> MapMaterializedValue<TMat2>(Func<TMat, TMat2> mapFunc)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TMat2">TBD</typeparam>
        /// <param name="sink">TBD</param>
        /// <param name="materializer">TBD</param>
        /// <exception cref="NotImplementedException">TBD</exception>
        /// <returns>TBD</returns>
        public override TMat2 RunWith<TMat2>(IGraph<SinkShape<TOut>, TMat2> sink, IMaterializer materializer)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TMat2">TBD</typeparam>
        /// <param name="sink">TBD</param>
        /// <returns>TBD</returns>
        public override TClosed To<TMat2>(IGraph<SinkShape<TOut>, TMat2> sink) => _finishFunction(Flow.To(sink));

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="parallelism">TBD</param>
        /// <returns>TBD</returns>
        public override IFlow<TOut, TMat> MergeSubstreamsWithParallelism(int parallelism) => _mergeBackFunction.Apply(Flow, parallelism);

        /// <summary>
        /// Change the attributes of this <see cref="Flow{TIn,TOut,TMat}"/> to the given ones. Note that this
        /// operation has no effect on an empty Flow (because the attributes apply
        /// only to the contained processing stages).
        /// </summary>
        /// <param name="attributes">TBD</param>
        /// <exception cref="NotSupportedException">TBD</exception>
        /// <returns>TBD</returns>
        public SubFlowImpl<TIn, TOut, TMat, TClosed> WithAttributes(Attributes attributes)
        {
            throw new NotSupportedException();
        }
    }
}
