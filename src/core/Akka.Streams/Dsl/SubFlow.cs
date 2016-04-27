//-----------------------------------------------------------------------
// <copyright file="SubFlow.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Reactive.Streams;
using Akka.Streams.Implementation.Stages;

namespace Akka.Streams.Dsl
{
    /// <summary>
    /// A “stream of streams” sub-flow of data elements, e.g. produced by <see cref="GroupBy{TIn,TKey}"/>.
    /// SubFlows cannot contribute to the super-flow’s materialized value since they
    /// are materialized later, during the runtime of the flow graph processing.
    /// </summary>
    public abstract class SubFlow<TOut, TMat, TClosed> : IFlow<TOut, TMat>
    {
        public abstract IFlow<T2, TMat> Via<T2, TMat2>(IGraph<FlowShape<TOut, T2>, TMat2> flow);

        public abstract IFlow<T2, TMat3> ViaMaterialized<T2, TMat2, TMat3>(IGraph<FlowShape<TOut, T2>, TMat2> flow, Func<TMat, TMat2, TMat3> combine);

        /// <summary>
        /// Connect this <see cref="Source{TOut,TMat}"/> to a <see cref="Sink{TIn,TMat}"/> and run it. The returned value is the materialized value
        /// of the <see cref="Sink{TIn,TMat}"/>, e.g. the <see cref="IPublisher{TIn}"/> of a <see cref="Sink.Publisher{TIn}"/>.
        /// </summary>
        public abstract TMat2 RunWith<TMat2>(IGraph<SinkShape<TOut>, TMat2> sink, IMaterializer materializer);

        /// <summary>
        /// Attach a <see cref="Sink"/> to each sub-flow, closing the overall Graph that is being
        /// constructed.
        /// </summary>
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
        public abstract IFlow<TOut, TMat> MergeSubstreamsWithParallelism(int parallelism);

        /// <summary>
        /// Flatten the sub-flows back into the super-flow by concatenating them.
        /// This is usually a bad idea when combined with <see cref="GroupBy{TIn,TKey}"/> since it can
        /// easily lead to deadlock—the concatenation does not consume from the second
        /// substream until the first has finished and the <see cref="GroupBy{TIn,TKey}"/> stage will get
        /// back-pressure from the second stream.
        /// </summary>
        public virtual IFlow<TOut, TMat> ConcatSubstream() => MergeSubstreamsWithParallelism(1);
    }
}