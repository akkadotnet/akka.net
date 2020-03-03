//-----------------------------------------------------------------------
// <copyright file="Stages.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Streams.Stage;
using Akka.Streams.Supervision;

namespace Akka.Streams.Implementation.Stages
{
    /// <summary>
    /// TBD
    /// </summary>
    public static class DefaultAttributes
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes IODispatcher = ActorAttributes.CreateDispatcher("akka.stream.default-blocking-io-dispatcher");

        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes Fused = Attributes.CreateName("fused");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes Select = Attributes.CreateName("select");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes Log = Attributes.CreateName("log");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes Where = Attributes.CreateName("where");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes Collect = Attributes.CreateName("collect");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes Sum = Attributes.CreateName("sum");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes Recover = Attributes.CreateName("recover");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes RecoverWith = Attributes.CreateName("recoverWith");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes MapAsync = Attributes.CreateName("mapAsync");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes MapAsyncUnordered = Attributes.CreateName("mapAsyncUnordered");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes Grouped = Attributes.CreateName("grouped");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes Limit = Attributes.CreateName("limit");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes LimitWeighted = Attributes.CreateName("limitWeighted");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes Sliding = Attributes.CreateName("sliding");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes Take = Attributes.CreateName("take");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes Drop = Attributes.CreateName("drop");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes Skip = Attributes.CreateName("skip");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes TakeWhile = Attributes.CreateName("takeWhile");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes SkipWhile = Attributes.CreateName("skipWhile");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes Scan = Attributes.CreateName("scan");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes ScanAsync = Attributes.CreateName("scanAsync");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes Aggregate = Attributes.CreateName("aggregate");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes AggregateAsync = Attributes.CreateName("aggregateAsync");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes Buffer = Attributes.CreateName("buffer");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes Batch = Attributes.CreateName("batch");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes BatchWeighted = Attributes.CreateName("batchWeighted");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes Conflate = Attributes.CreateName("conflate");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes Expand = Attributes.CreateName("expand");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes StatefulSelectMany = Attributes.CreateName("statefulSelectMany");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes GroupBy = Attributes.CreateName("groupBy");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes PrefixAndTail = Attributes.CreateName("prefixAndTail");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes Split = Attributes.CreateName("split");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes ConcatAll = Attributes.CreateName("concatAll");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes Processor = Attributes.CreateName("processor");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes ProcessorWithKey = Attributes.CreateName("processorWithKey");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes IdentityOp = Attributes.CreateName("identityOp");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes DelimiterFraming = Attributes.CreateName("delimiterFraming");

        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes Initial = Attributes.CreateName("initial");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes Completion = Attributes.CreateName("completion");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes Idle = Attributes.CreateName("idle");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes IdleTimeoutBidi = Attributes.CreateName("idleTimeoutBidi");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes DelayInitial = Attributes.CreateName("delayInitial");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes IdleInject = Attributes.CreateName("idleInject");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes BackpressureTimeout = Attributes.CreateName("backpressureTimeout");

        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes Merge = Attributes.CreateName("merge");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes MergePreferred = Attributes.CreateName("mergePreferred");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes FlattenMerge = Attributes.CreateName("flattenMerge");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes Broadcast = Attributes.CreateName("broadcast");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes Balance = Attributes.CreateName("balance");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes Zip = Attributes.CreateName("zip");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes Unzip = Attributes.CreateName("unzip");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes Concat = Attributes.CreateName("concat");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes OrElse = Attributes.CreateName("orElse");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes Repeat = Attributes.CreateName("repeat");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes Unfold = Attributes.CreateName("unfold");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes UnfoldAsync = Attributes.CreateName("unfoldAsync");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes UnfoldInf = Attributes.CreateName("unfoldInf");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes UnfoldResourceSource = Attributes.CreateName("unfoldResourceSource").And(IODispatcher);
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes UnfoldResourceSourceAsync = Attributes.CreateName("unfoldResourceSourceAsync").And(IODispatcher);
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes TerminationWatcher = Attributes.CreateName("terminationWatcher");
        public static readonly Attributes Watch = Attributes.CreateName("watch");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes Delay = Attributes.CreateName("delay");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes ZipN = Attributes.CreateName("zipN");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes ZipWithN = Attributes.CreateName("zipWithN");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes ZipWithIndex = Attributes.CreateName("zipWithIndex");

        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes PublisherSource = Attributes.CreateName("publisherSource");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes EnumerableSource = Attributes.CreateName("enumerableSource");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes CycledSource = Attributes.CreateName("cycledSource");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes TaskSource = Attributes.CreateName("taskSource");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes TickSource = Attributes.CreateName("tickSource");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes SingleSource = Attributes.CreateName("singleSource");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes EmptySource = Attributes.CreateName("emptySource");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes MaybeSource = Attributes.CreateName("maybeSource");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes FailedSource = Attributes.CreateName("failedSource");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes ConcatSource = Attributes.CreateName("concatSource");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes ConcatMaterializedSource = Attributes.CreateName("concatMaterializedSource");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes SubscriberSource = Attributes.CreateName("subscriberSource");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes ActorPublisherSource = Attributes.CreateName("actorPublisherSource");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes ActorRefSource = Attributes.CreateName("actorRefSource");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes QueueSource = Attributes.CreateName("queueSource");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes InputStreamSource = Attributes.CreateName("inputStreamSource").And(IODispatcher);
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes OutputStreamSource = Attributes.CreateName("outputStreamSource").And(IODispatcher);
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes FileSource = Attributes.CreateName("fileSource").And(IODispatcher);

        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes SubscriberSink = Attributes.CreateName("subscriberSink");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes CancelledSink = Attributes.CreateName("cancelledSink");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes FirstSink = Attributes.CreateName("firstSink").And(Attributes.CreateInputBuffer(initial: 1, max: 1));
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes FirstOrDefaultSink = Attributes.CreateName("firstOrDefaultSink").And(Attributes.CreateInputBuffer(initial: 1, max: 1));
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes LastSink = Attributes.CreateName("lastSink");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes LastOrDefaultSink = Attributes.CreateName("lastOrDefaultSink");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes PublisherSink = Attributes.CreateName("publisherSink");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes FanoutPublisherSink = Attributes.CreateName("fanoutPublisherSink");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes IgnoreSink = Attributes.CreateName("ignoreSink");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes ActorRefSink = Attributes.CreateName("actorRefSink");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes ActorRefWithAck = Attributes.CreateName("actorRefWithAckSink");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes ActorSubscriberSink = Attributes.CreateName("actorSubscriberSink");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes QueueSink = Attributes.CreateName("queueSink");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes LazySink = Attributes.CreateName("lazySink");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes LazySource = Attributes.CreateName("lazySource");
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes InputStreamSink = Attributes.CreateName("inputStreamSink").And(IODispatcher);
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes OutputStreamSink = Attributes.CreateName("outputStreamSink").And(IODispatcher);
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes FileSink = Attributes.CreateName("fileSink").And(IODispatcher);
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes SeqSink = Attributes.CreateName("seqSink");
    }

    /// <summary>
    /// Stage that is backed by a GraphStage but can be symbolically introspected
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    public sealed class SymbolicGraphStage<TIn, TOut> : PushPullGraphStage<TIn, TOut>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="symbolicStage">TBD</param>
        public SymbolicGraphStage(ISymbolicStage<TIn, TOut> symbolicStage) : base(symbolicStage.Create, symbolicStage.Attributes)
        {
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    public interface ISymbolicStage<in TIn, out TOut> : IStage<TIn, TOut>
    {
        /// <summary>
        /// TBD
        /// </summary>
        Attributes Attributes { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="effectiveAttributes">TBD</param>
        /// <returns>TBD</returns>
        IStage<TIn, TOut> Create(Attributes effectiveAttributes);
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    public abstract class SymbolicStage<TIn, TOut> : ISymbolicStage<TIn, TOut>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="attributes">TBD</param>
        protected SymbolicStage(Attributes attributes)
        {
            Attributes = attributes;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public Attributes Attributes { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="effectiveAttributes">TBD</param>
        /// <returns>TBD</returns>
        public abstract IStage<TIn, TOut> Create(Attributes effectiveAttributes);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="attributes">TBD</param>
        /// <returns>TBD</returns>
        protected Decider Supervision(Attributes attributes)
            => attributes.GetAttribute(new ActorAttributes.SupervisionStrategy(Deciders.StoppingDecider)).Decider;
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    public sealed class FirstOrDefault<TIn> : GraphStageWithMaterializedValue<SinkShape<TIn>, Task<TIn>>
    {
        #region internal classes
        
        private sealed class Logic : InGraphStageLogic
        {
            private readonly FirstOrDefault<TIn> _stage;
            private readonly TaskCompletionSource<TIn> _promise = new TaskCompletionSource<TIn>();

            public Task<TIn> Task => _promise.Task;

            public Logic(FirstOrDefault<TIn> stage) : base(stage.Shape)
            {
                _stage = stage;

                SetHandler(stage._in, this);
            }

            public override void OnPush()
            {
                _promise.TrySetResult(Grab(_stage._in));
                CompleteStage();
            }

            public override void OnUpstreamFinish()
            {
                if (_stage._throwOnDefault)
                    _promise.TrySetException(new NoSuchElementException("First of empty stream"));
                else
                    _promise.TrySetResult(default(TIn));

                CompleteStage();
            }

            public override void OnUpstreamFailure(Exception e)
            {
                _promise.TrySetException(e);
                FailStage(e);
            }

            public override void PreStart() => Pull(_stage._in);
        }

        #endregion
        
        private readonly bool _throwOnDefault;
        private readonly Inlet<TIn> _in = new Inlet<TIn>("firstOrDefault.in");

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="throwOnDefault">TBD</param>
        public FirstOrDefault(bool throwOnDefault = false)
        {
            _throwOnDefault = throwOnDefault;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override SinkShape<TIn> Shape => new SinkShape<TIn>(_in);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        public override ILogicAndMaterializedValue<Task<TIn>> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var logic = new Logic(this);
            return new LogicAndMaterializedValue<Task<TIn>>(logic, logic.Task);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => "FirstOrDefaultStage";
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    public sealed class LastOrDefault<TIn> : GraphStageWithMaterializedValue<SinkShape<TIn>, Task<TIn>>
    {
        #region internal classes

        private sealed class Logic : InGraphStageLogic
        {
            private readonly LastOrDefault<TIn> _stage;
            private readonly TaskCompletionSource<TIn> _promise = new TaskCompletionSource<TIn>();
            private TIn _prev;
            private bool _foundAtLeastOne;

            public Task<TIn> Task => _promise.Task;

            public Logic(LastOrDefault<TIn> stage) : base(stage.Shape)
            {
                _stage = stage;

                SetHandler(stage._in, this);
            }

            public override void OnPush()
            {
                _prev = Grab(_stage._in);
                _foundAtLeastOne = true;
                Pull(_stage._in);
            }

            public override void OnUpstreamFinish()
            {
                if (_stage._throwOnDefault && !_foundAtLeastOne)
                    _promise.TrySetException(new NoSuchElementException("Last of empty stream"));
                else
                    _promise.TrySetResult(_prev);

                CompleteStage();
            }

            public override void OnUpstreamFailure(Exception e)
            {
                _promise.TrySetException(e);
                FailStage(e);
            }


            public override void PreStart() => Pull(_stage._in);
        }

        #endregion

        private readonly bool _throwOnDefault;
        private readonly Inlet<TIn> _in = new Inlet<TIn>("lastOrDefault.in");

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="throwOnDefault">TBD</param>
        public LastOrDefault(bool throwOnDefault = false)
        {
            _throwOnDefault = throwOnDefault;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override SinkShape<TIn> Shape => new SinkShape<TIn>(_in);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        public override ILogicAndMaterializedValue<Task<TIn>> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var logic = new Logic(this);
            return new LogicAndMaterializedValue<Task<TIn>>(logic, logic.Task);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => "LastOrDefaultStage";
    }
}
