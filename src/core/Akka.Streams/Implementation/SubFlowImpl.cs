using System;
using System.Reactive.Streams;
using Akka.Streams.Dsl;

namespace Akka.Streams.Implementation
{
    public interface IMergeBack<TIn>
    {
        IFlow<TOut, Unit> Apply<TOut>(Flow<TIn, TOut, Unit> flow, int breadth);
    }
    
    public class SubFlowImpl<TIn, TOut, TMat> : SubFlow<TOut, TMat>
    {
        private readonly IMergeBack<TIn> _mergeBackFunction;
        private readonly Func<Sink<TIn, Unit>, IFlow<TOut, TMat>> _finishFunction;

        public SubFlowImpl(Flow<TIn, TOut, Unit> flow, IMergeBack<TIn> mergeBackFunction, Func<Sink<TIn, Unit>, IFlow<TOut, TMat>> finishFunction)
        {
            _mergeBackFunction = mergeBackFunction;
            _finishFunction = finishFunction;
            Flow = flow;
        }

        public Flow<TIn, TOut, Unit> Flow { get; }

        public override IFlow<T2, TMat> Via<T2, TMat2>(IGraph<FlowShape<TOut, T2>, TMat2> flow)
        {
            return new SubFlowImpl<TIn, T2, TMat>(Flow.Via(flow), _mergeBackFunction, sink => (IFlow<T2, TMat>)_finishFunction(sink));
        }

        public override IFlow<T2, TMat3> ViaMaterialized<T2, TMat2, TMat3>(IGraph<FlowShape<TOut, T2>, TMat2> flow, Func<TMat, TMat2, TMat3> combine)
        {
            throw new NotImplementedException();
        }

        public override IFlow<TOut, TMat> To<TMat2>(IGraph<SinkShape<TOut>, TMat2> sink)
        {
            var result = _finishFunction(Flow.To(sink));
            return result;
        }

        public override IFlow<TOut, Unit> MergeSubstreamsWithParallelism(int parallelism)
        {
            return _mergeBackFunction.Apply(Flow, parallelism);
        }
    }
}