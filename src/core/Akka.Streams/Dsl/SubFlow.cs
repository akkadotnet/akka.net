using System;
using Akka.Streams.Implementation;
using Akka.Streams.Implementation.Stages;

namespace Akka.Streams.Dsl
{
    /// <summary>
    /// A “stream of streams” sub-flow of data elements, e.g. produced by <see cref="GroupBy"/>.
    /// SubFlows cannot contribute to the super-flow’s materialized value since they
    /// are materialized later, during the runtime of the flow graph processing.
    /// </summary>
    public abstract class SubFlow<TOut, TMat, TFun, TClosed> : FlowBase<TOut, TMat>
    {
        /// <summary>
        /// Attach a <see cref="Sink"/> to each sub-flow, closing the overall Graph that is being
        /// constructed.
        /// </summary>
        public abstract TClosed To<TMat2>(IGraph<SinkShape<TOut>, TMat> sink);

        /// <summary>
        /// Flatten the sub-flows back into the super-flow by performing a merge
        /// without parallelism limit (i.e. having an unbounded number of sub-flows
        /// active concurrently).
        /// </summary>
        public virtual TFun MergeSubstreams()
        {
            return MergeSubstreamsWithParallelism(int.MaxValue);
        }

        /// <summary>
        /// Flatten the sub-flows back into the super-flow by performing a merge
        /// with the given parallelism limit. This means that only up to <paramref name="parallelism"/>
        /// substreams will be executed at any given time. Substreams that are not
        /// yet executed are also not materialized, meaning that back-pressure will
        /// be exerted at the operator that creates the substreams when the parallelism
        /// limit is reached.
        /// </summary>
        public abstract TFun MergeSubstreamsWithParallelism(int parallelism);

        /// <summary>
        /// Flatten the sub-flows back into the super-flow by concatenating them.
        /// This is usually a bad idea when combined with <see cref="GroupBy"/> since it can
        /// easily lead to deadlock—the concatenation does not consume from the second
        /// substream until the first has finished and the <see cref="GroupBy"/> stage will get
        /// back-pressure from the second stream.
        /// </summary>
        public virtual TFun ConcatSubstream()
        {
            return MergeSubstreamsWithParallelism(1);
        }

        public override IFlow<T, TMat> AndThen<T>(StageModule op)
        {
            throw new NotImplementedException();
        }

        public override IFlow<T, TMat> AndThenMat<T>(MaterializingStageFactory<T, TOut, TMat> op)
        {
            throw new NotImplementedException();
        }

        public override IFlow<TOut, TMat> WithAttributes(Attributes attributes)
        {
            throw new NotImplementedException();
        }

        public override IFlow<T, TMat3> ViaMat<T, TMat2, TMat3>(IGraph<FlowShape<TOut, T>, TMat2> flow, Func<TMat, TMat2, TMat3> combine)
        {
            throw new NotImplementedException();
        }
    }
}