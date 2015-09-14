using System;
using System.Reactive.Streams;
using Akka.Streams.Dsl;

namespace Akka.Streams.Implementation
{
    public delegate TFun MergeBack<TIn, out TFun, T>(Flow<TIn, T, Unit> flow, int breadth);

    public class SubFlowImpl<TIn, TOut, TMat, TFun, TClosed> : SubFlow<TOut, TMat, TFun, TClosed>
    {
        private readonly MergeBack<TIn, TFun, object> _mergeBackFunction;
        private readonly Func<Sink<TIn, Unit>, TClosed> _finishFunction;

        public SubFlowImpl(Flow<TIn, TOut, Unit> flow, MergeBack<TIn, TFun, object> mergeBackFunction, Func<Sink<TIn, Unit>, TClosed> finishFunction)
        {
            _mergeBackFunction = mergeBackFunction;
            _finishFunction = finishFunction;
            Flow = flow;
        }

        public Flow<TIn, TOut, Unit> Flow { get; }


        public override TClosed To<TMat2>(IGraph<SinkShape<TOut>, TMat> sink)
        {
            throw new NotImplementedException();
        }

        public override TFun MergeSubstreamsWithParallelism(int parallelism)
        {
            throw new NotImplementedException();
        }
    }
}