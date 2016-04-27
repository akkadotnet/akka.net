//-----------------------------------------------------------------------
// <copyright file="SubFlowImpl.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Streams.Dsl;

namespace Akka.Streams.Implementation
{
    public interface IMergeBack<TIn, TMat>
    {
        IFlow<TOut, TMat> Apply<TOut>(Flow<TIn, TOut, TMat> flow, int breadth);
    }
    
    public class SubFlowImpl<TIn, TOut, TMat, TClosed> : SubFlow<TOut, TMat, TClosed>
    {
        private readonly IMergeBack<TIn, TMat> _mergeBackFunction;
        private readonly Func<Sink<TIn, TMat>, TClosed> _finishFunction;

        public SubFlowImpl(Flow<TIn, TOut, TMat> flow, IMergeBack<TIn, TMat> mergeBackFunction, Func<Sink<TIn, TMat>, TClosed> finishFunction)
        {
            _mergeBackFunction = mergeBackFunction;
            _finishFunction = finishFunction;
            Flow = flow;
        }

        public Flow<TIn, TOut, TMat> Flow { get; }

        public override IFlow<T2, TMat> Via<T2, TMat2>(IGraph<FlowShape<TOut, T2>, TMat2> flow) =>
                new SubFlowImpl<TIn, T2, TMat, TClosed>(Flow.Via(flow), _mergeBackFunction,
                    sink => _finishFunction(sink));

        public override IFlow<T2, TMat3> ViaMaterialized<T2, TMat2, TMat3>(IGraph<FlowShape<TOut, T2>, TMat2> flow, Func<TMat, TMat2, TMat3> combine)
        {
            throw new NotImplementedException();
        }

        public override TMat2 RunWith<TMat2>(IGraph<SinkShape<TOut>, TMat2> sink, IMaterializer materializer)
        {
            throw new NotImplementedException();
        }

        public override TClosed To<TMat2>(IGraph<SinkShape<TOut>, TMat2> sink) => _finishFunction(Flow.To(sink));

        public override IFlow<TOut, TMat> MergeSubstreamsWithParallelism(int parallelism) => _mergeBackFunction.Apply(Flow, parallelism);

        /// <summary>
        /// Change the attributes of this <see cref="Flow{TIn,TOut,TMat}"/> to the given ones. Note that this
        /// operation has no effect on an empty Flow (because the attributes apply
        /// only to the contained processing stages).
        /// </summary>
        public SubFlowImpl<TIn, TOut, TMat, TClosed> WithAttributes(Attributes attributes)
        {
            throw new NotSupportedException();
        }
    }
}