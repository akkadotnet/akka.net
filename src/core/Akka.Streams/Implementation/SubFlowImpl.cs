using System;
using System.Reactive.Streams;
using Akka.Streams.Dsl;

namespace Akka.Streams.Implementation
{
    public interface IMergeBack<TIn, TMat>
    {
        IFlow<TOut, TMat> Apply<TOut>(Flow<TIn, TOut, TMat> flow, int breadth);
    }
    
    public class SubFlowImpl<TIn, TOut, TMat> : SubFlow<TOut, TMat>
    {
        private readonly IMergeBack<TIn, TMat> _mergeBackFunction;
        private readonly Func<Sink<TIn, TMat>, IFlow<TOut, TMat>> _finishFunction;

        public SubFlowImpl(Flow<TIn, TOut, TMat> flow, IMergeBack<TIn, TMat> mergeBackFunction, Func<Sink<TIn, TMat>, IFlow<TOut, TMat>> finishFunction)
        {
            _mergeBackFunction = mergeBackFunction;
            _finishFunction = finishFunction;
            Flow = flow;
        }

        public Flow<TIn, TOut, TMat> Flow { get; }

        public override IFlow<T2, TMat> Via<T2, TMat2>(IGraph<FlowShape<TOut, T2>, TMat2> flow)
        {
            return new SubFlowImpl<TIn, T2, TMat>(Flow.Via(flow), _mergeBackFunction, sink => (IFlow<T2, TMat>)_finishFunction(sink));
        }

        public override IFlow<T2, TMat3> ViaMaterialized<T2, TMat2, TMat3>(IGraph<FlowShape<TOut, T2>, TMat2> flow, Func<TMat, TMat2, TMat3> combine)
        {
            throw new NotImplementedException();
        }

        public override TMat2 RunWith<TMat2>(IGraph<SinkShape<TOut>, TMat2> sink, IMaterializer materializer)
        {
            throw new NotImplementedException();
        }

        public override IFlow<TOut, TMat> To<TMat2>(IGraph<SinkShape<TOut>, TMat2> sink)
        {
            var result = _finishFunction(Flow.To(sink));
            return result;
        }

        public override IFlow<TOut, TMat> MergeSubstreamsWithParallelism(int parallelism)
        {
            return _mergeBackFunction.Apply(Flow, parallelism);
        }

        public TMat Run(ActorMaterializer materializer)
        {
            throw new NotImplementedException();
        }
    }
}