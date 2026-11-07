//-----------------------------------------------------------------------
// <copyright file="Stages.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Dispatch;
using Akka.Streams.Stage;
using Akka.Streams.Supervision;
using Akka.Util.Internal;

namespace Akka.Streams.Implementation.Stages
{
    /// <summary>
    /// Provides default attributes used to identify built-in stream stages and configure selected stage dispatchers.
    /// </summary>
    public static class DefaultAttributes
    {
        /// <summary>
        /// Provides the dispatcher attribute used by stream I/O stages.
        /// </summary>
        public static readonly Attributes IODispatcher = ActorAttributes.CreateDispatcher(ActorAttributes.IODispatcher.Name);

        /// <summary>
        /// Provides the default stage attribute named `fused`.
        /// </summary>
        public static readonly Attributes Fused = Attributes.CreateName("fused");
        /// <summary>
        /// Provides the default stage attribute named `select`.
        /// </summary>
        public static readonly Attributes Select = Attributes.CreateName("select");
        /// <summary>
        /// Provides the default stage attribute named `log`.
        /// </summary>
        public static readonly Attributes Log = Attributes.CreateName("log");
        /// <summary>
        /// Provides the default stage attribute named `where`.
        /// </summary>
        public static readonly Attributes Where = Attributes.CreateName("where");
        /// <summary>
        /// Provides the default stage attribute named `collect`.
        /// </summary>
        public static readonly Attributes Collect = Attributes.CreateName("collect");
        /// <summary>
        /// Provides the default stage attribute named `sum`.
        /// </summary>
        public static readonly Attributes Sum = Attributes.CreateName("sum");
        /// <summary>
        /// Provides the default stage attribute named `recover`.
        /// </summary>
        public static readonly Attributes Recover = Attributes.CreateName("recover");
        /// <summary>
        /// Provides the default stage attribute named `recoverWith`.
        /// </summary>
        public static readonly Attributes RecoverWith = Attributes.CreateName("recoverWith");
        /// <summary>
        /// Provides the default stage attribute named `mapAsync`.
        /// </summary>
        public static readonly Attributes MapAsync = Attributes.CreateName("mapAsync");
        /// <summary>
        /// Provides the default stage attribute named `mapAsyncUnordered`.
        /// </summary>
        public static readonly Attributes MapAsyncUnordered = Attributes.CreateName("mapAsyncUnordered");
        /// <summary>
        /// Provides the default stage attribute named `grouped`.
        /// </summary>
        public static readonly Attributes Grouped = Attributes.CreateName("grouped");
        /// <summary>
        /// Provides the default stage attribute named `groupedWithin`.
        /// </summary>
        public static readonly Attributes GroupedWithin = Attributes.CreateName("groupedWithin");
        /// <summary>
        /// Provides the default stage attribute named `groupedWeightedWithin`.
        /// </summary>
        public static readonly Attributes GroupedWeightedWithin = Attributes.CreateName("groupedWeightedWithin");
        /// <summary>
        /// Provides the default stage attribute named `limit`.
        /// </summary>
        public static readonly Attributes Limit = Attributes.CreateName("limit");
        /// <summary>
        /// Provides the default stage attribute named `limitWeighted`.
        /// </summary>
        public static readonly Attributes LimitWeighted = Attributes.CreateName("limitWeighted");
        /// <summary>
        /// Provides the default stage attribute named `sliding`.
        /// </summary>
        public static readonly Attributes Sliding = Attributes.CreateName("sliding");
        /// <summary>
        /// Provides the default stage attribute named `take`.
        /// </summary>
        public static readonly Attributes Take = Attributes.CreateName("take");
        /// <summary>
        /// Provides the default stage attribute named `drop`.
        /// </summary>
        public static readonly Attributes Drop = Attributes.CreateName("drop");
        /// <summary>
        /// Provides the default stage attribute named `skip`.
        /// </summary>
        public static readonly Attributes Skip = Attributes.CreateName("skip");
        /// <summary>
        /// Provides the default stage attribute named `takeWhile`.
        /// </summary>
        public static readonly Attributes TakeWhile = Attributes.CreateName("takeWhile");
        /// <summary>
        /// Provides the default stage attribute named `skipWhile`.
        /// </summary>
        public static readonly Attributes SkipWhile = Attributes.CreateName("skipWhile");
        /// <summary>
        /// Provides the default stage attribute named `scan`.
        /// </summary>
        public static readonly Attributes Scan = Attributes.CreateName("scan");
        /// <summary>
        /// Provides the default stage attribute named `scanAsync`.
        /// </summary>
        public static readonly Attributes ScanAsync = Attributes.CreateName("scanAsync");
        /// <summary>
        /// Provides the default stage attribute named `aggregate`.
        /// </summary>
        public static readonly Attributes Aggregate = Attributes.CreateName("aggregate");
        /// <summary>
        /// Provides the default stage attribute named `aggregateAsync`.
        /// </summary>
        public static readonly Attributes AggregateAsync = Attributes.CreateName("aggregateAsync");
        /// <summary>
        /// Provides the default stage attribute named `buffer`.
        /// </summary>
        public static readonly Attributes Buffer = Attributes.CreateName("buffer");
        /// <summary>
        /// Provides the default stage attribute named `batch`.
        /// </summary>
        public static readonly Attributes Batch = Attributes.CreateName("batch");
        /// <summary>
        /// Provides the default stage attribute named `batchWeighted`.
        /// </summary>
        public static readonly Attributes BatchWeighted = Attributes.CreateName("batchWeighted");
        /// <summary>
        /// Provides the default stage attribute named `conflate`.
        /// </summary>
        public static readonly Attributes Conflate = Attributes.CreateName("conflate");
        /// <summary>
        /// Provides the default stage attribute named `expand`.
        /// </summary>
        public static readonly Attributes Expand = Attributes.CreateName("expand");
        /// <summary>
        /// Provides the default stage attribute named `statefulSelectMany`.
        /// </summary>
        public static readonly Attributes StatefulSelectMany = Attributes.CreateName("statefulSelectMany");
        /// <summary>
        /// Provides the default stage attribute named `groupBy`.
        /// </summary>
        public static readonly Attributes GroupBy = Attributes.CreateName("groupBy");
        /// <summary>
        /// Provides the default stage attribute named `prefixAndTail`.
        /// </summary>
        public static readonly Attributes PrefixAndTail = Attributes.CreateName("prefixAndTail");
        /// <summary>
        /// Provides the default stage attribute named `split`.
        /// </summary>
        public static readonly Attributes Split = Attributes.CreateName("split");
        /// <summary>
        /// Provides the default stage attribute named `concatAll`.
        /// </summary>
        public static readonly Attributes ConcatAll = Attributes.CreateName("concatAll");
        /// <summary>
        /// Provides the default stage attribute named `processor`.
        /// </summary>
        public static readonly Attributes Processor = Attributes.CreateName("processor");
        /// <summary>
        /// Provides the default stage attribute named `processorWithKey`.
        /// </summary>
        public static readonly Attributes ProcessorWithKey = Attributes.CreateName("processorWithKey");
        /// <summary>
        /// Provides the default stage attribute named `identityOp`.
        /// </summary>
        public static readonly Attributes IdentityOp = Attributes.CreateName("identityOp");
        /// <summary>
        /// Provides the default stage attribute named `delimiterFraming`.
        /// </summary>
        public static readonly Attributes DelimiterFraming = Attributes.CreateName("delimiterFraming");

        /// <summary>
        /// Provides the default stage attribute named `initial`.
        /// </summary>
        public static readonly Attributes Initial = Attributes.CreateName("initial");
        /// <summary>
        /// Provides the default stage attribute named `completion`.
        /// </summary>
        public static readonly Attributes Completion = Attributes.CreateName("completion");
        /// <summary>
        /// Provides the default stage attribute named `idle`.
        /// </summary>
        public static readonly Attributes Idle = Attributes.CreateName("idle");
        /// <summary>
        /// Provides the default stage attribute named `idleTimeoutBidi`.
        /// </summary>
        public static readonly Attributes IdleTimeoutBidi = Attributes.CreateName("idleTimeoutBidi");
        /// <summary>
        /// Provides the default stage attribute named `delayInitial`.
        /// </summary>
        public static readonly Attributes DelayInitial = Attributes.CreateName("delayInitial");
        /// <summary>
        /// Provides the default stage attribute named `idleInject`.
        /// </summary>
        public static readonly Attributes IdleInject = Attributes.CreateName("idleInject");
        /// <summary>
        /// Provides the default stage attribute named `backpressureTimeout`.
        /// </summary>
        public static readonly Attributes BackpressureTimeout = Attributes.CreateName("backpressureTimeout");

        /// <summary>
        /// Provides the default stage attribute named `merge`.
        /// </summary>
        public static readonly Attributes Merge = Attributes.CreateName("merge");
        /// <summary>
        /// Provides the default stage attribute named `mergePreferred`.
        /// </summary>
        public static readonly Attributes MergePreferred = Attributes.CreateName("mergePreferred");
        /// <summary>
        /// Provides the default stage attribute named `flattenMerge`.
        /// </summary>
        public static readonly Attributes FlattenMerge = Attributes.CreateName("flattenMerge");
        /// <summary>
        /// Provides the default stage attribute named `broadcast`.
        /// </summary>
        public static readonly Attributes Broadcast = Attributes.CreateName("broadcast");
        /// <summary>
        /// Provides the default stage attribute named `balance`.
        /// </summary>
        public static readonly Attributes Balance = Attributes.CreateName("balance");
        /// <summary>
        /// Provides the default stage attribute named `zip`.
        /// </summary>
        public static readonly Attributes Zip = Attributes.CreateName("zip");
        /// <summary>
        /// Provides the default stage attribute named `unzip`.
        /// </summary>
        public static readonly Attributes Unzip = Attributes.CreateName("unzip");
        /// <summary>
        /// Provides the default stage attribute named `concat`.
        /// </summary>
        public static readonly Attributes Concat = Attributes.CreateName("concat");
        /// <summary>
        /// Provides the default stage attribute named `orElse`.
        /// </summary>
        public static readonly Attributes OrElse = Attributes.CreateName("orElse");
        /// <summary>
        /// Provides the default stage attribute named `repeat`.
        /// </summary>
        public static readonly Attributes Repeat = Attributes.CreateName("repeat");
        /// <summary>
        /// Provides the default stage attribute named `unfold`.
        /// </summary>
        public static readonly Attributes Unfold = Attributes.CreateName("unfold");
        /// <summary>
        /// Provides the default stage attribute named `unfoldAsync`.
        /// </summary>
        public static readonly Attributes UnfoldAsync = Attributes.CreateName("unfoldAsync");
        /// <summary>
        /// Provides the default stage attribute named `unfoldInf`.
        /// </summary>
        public static readonly Attributes UnfoldInf = Attributes.CreateName("unfoldInf");
        /// <summary>
        /// Provides the `unfoldResourceSource` stage attribute together with the I/O dispatcher attribute.
        /// </summary>
        public static readonly Attributes UnfoldResourceSource = Attributes.CreateName("unfoldResourceSource").And(IODispatcher);
        /// <summary>
        /// Provides the `unfoldResourceSourceAsync` stage attribute together with the I/O dispatcher attribute.
        /// </summary>
        public static readonly Attributes UnfoldResourceSourceAsync = Attributes.CreateName("unfoldResourceSourceAsync").And(IODispatcher);
        /// <summary>
        /// Provides the default stage attribute named `terminationWatcher`.
        /// </summary>
        public static readonly Attributes TerminationWatcher = Attributes.CreateName("terminationWatcher");
        public static readonly Attributes Watch = Attributes.CreateName("watch");
        /// <summary>
        /// Provides the default stage attribute named `delay`.
        /// </summary>
        public static readonly Attributes Delay = Attributes.CreateName("delay");
        /// <summary>
        /// Provides the default stage attribute named `zipN`.
        /// </summary>
        public static readonly Attributes ZipN = Attributes.CreateName("zipN");
        /// <summary>
        /// Provides the default stage attribute named `zipWithN`.
        /// </summary>
        public static readonly Attributes ZipWithN = Attributes.CreateName("zipWithN");
        /// <summary>
        /// Provides the default stage attribute named `zipWithIndex`.
        /// </summary>
        public static readonly Attributes ZipWithIndex = Attributes.CreateName("zipWithIndex");

        /// <summary>
        /// Provides the default stage attribute named `publisherSource`.
        /// </summary>
        public static readonly Attributes PublisherSource = Attributes.CreateName("publisherSource");
        /// <summary>
        /// Provides the default stage attribute named `enumerableSource`.
        /// </summary>
        public static readonly Attributes EnumerableSource = Attributes.CreateName("enumerableSource");
        /// <summary>
        /// Provides the default stage attribute named `cycledSource`.
        /// </summary>
        public static readonly Attributes CycledSource = Attributes.CreateName("cycledSource");
        /// <summary>
        /// Provides the default stage attribute named `taskSource`.
        /// </summary>
        public static readonly Attributes TaskSource = Attributes.CreateName("taskSource");
                /// <summary>
        /// Provides the default stage attribute named `taskFlattenSource`.
        /// </summary>
        public static readonly Attributes TaskFlattenSource = Attributes.CreateName("taskFlattenSource");
        /// <summary>
        /// Provides the default stage attribute named `tickSource`.
        /// </summary>
        public static readonly Attributes TickSource = Attributes.CreateName("tickSource");
        /// <summary>
        /// Provides the default stage attribute named `singleSource`.
        /// </summary>
        public static readonly Attributes SingleSource = Attributes.CreateName("singleSource");
        /// <summary>
        /// Provides the default stage attribute named `emptySource`.
        /// </summary>
        public static readonly Attributes EmptySource = Attributes.CreateName("emptySource");
        /// <summary>
        /// Provides the default stage attribute named `maybeSource`.
        /// </summary>
        public static readonly Attributes MaybeSource = Attributes.CreateName("maybeSource");
        /// <summary>
        /// Provides the default stage attribute named `neverSource`.
        /// </summary>
        public static readonly Attributes NeverSource = Attributes.CreateName("neverSource");
        /// <summary>
        /// Provides the default stage attribute named `failedSource`.
        /// </summary>
        public static readonly Attributes FailedSource = Attributes.CreateName("failedSource");
        /// <summary>
        /// Provides the default stage attribute named `concatSource`.
        /// </summary>
        public static readonly Attributes ConcatSource = Attributes.CreateName("concatSource");
        /// <summary>
        /// Provides the default stage attribute named `concatMaterializedSource`.
        /// </summary>
        public static readonly Attributes ConcatMaterializedSource = Attributes.CreateName("concatMaterializedSource");
        /// <summary>
        /// Provides the default stage attribute named `subscriberSource`.
        /// </summary>
        public static readonly Attributes SubscriberSource = Attributes.CreateName("subscriberSource");
        /// <summary>
        /// Provides the default stage attribute named `actorPublisherSource`.
        /// </summary>
        public static readonly Attributes ActorPublisherSource = Attributes.CreateName("actorPublisherSource");
        /// <summary>
        /// Provides the default stage attribute named `actorRefSource`.
        /// </summary>
        public static readonly Attributes ActorRefSource = Attributes.CreateName("actorRefSource");
        /// <summary>
        /// Provides the default stage attribute named `queueSource`.
        /// </summary>
        public static readonly Attributes QueueSource = Attributes.CreateName("queueSource");
        /// <summary>
        /// Provides the `inputStreamSource` stage attribute together with the I/O dispatcher attribute.
        /// </summary>
        public static readonly Attributes InputStreamSource = Attributes.CreateName("inputStreamSource").And(IODispatcher);
        /// <summary>
        /// Provides the `outputStreamSource` stage attribute together with the I/O dispatcher attribute.
        /// </summary>
        public static readonly Attributes OutputStreamSource = Attributes.CreateName("outputStreamSource").And(IODispatcher);
        /// <summary>
        /// Provides the `fileSource` stage attribute together with the I/O dispatcher attribute.
        /// </summary>
        public static readonly Attributes FileSource = Attributes.CreateName("fileSource").And(IODispatcher);

        /// <summary>
        /// Provides the default stage attribute named `subscriberSink`.
        /// </summary>
        public static readonly Attributes SubscriberSink = Attributes.CreateName("subscriberSink");
        /// <summary>
        /// Provides the default stage attribute named `cancelledSink`.
        /// </summary>
        public static readonly Attributes CancelledSink = Attributes.CreateName("cancelledSink");
        /// <summary>
        /// Provides the `firstSink` stage attribute together with an input buffer configured with initial and maximum sizes of one.
        /// </summary>
        public static readonly Attributes FirstSink = Attributes.CreateName("firstSink").And(Attributes.CreateInputBuffer(initial: 1, max: 1));
        /// <summary>
        /// Provides the `firstOrDefaultSink` stage attribute together with an input buffer configured with initial and maximum sizes of one.
        /// </summary>
        public static readonly Attributes FirstOrDefaultSink = Attributes.CreateName("firstOrDefaultSink").And(Attributes.CreateInputBuffer(initial: 1, max: 1));
        /// <summary>
        /// Provides the default stage attribute named `lastSink`.
        /// </summary>
        public static readonly Attributes LastSink = Attributes.CreateName("lastSink");
        /// <summary>
        /// Provides the default stage attribute named `lastOrDefaultSink`.
        /// </summary>
        public static readonly Attributes LastOrDefaultSink = Attributes.CreateName("lastOrDefaultSink");
        /// <summary>
        /// Provides the default stage attribute named `publisherSink`.
        /// </summary>
        public static readonly Attributes PublisherSink = Attributes.CreateName("publisherSink");
        /// <summary>
        /// Provides the default stage attribute named `fanoutPublisherSink`.
        /// </summary>
        public static readonly Attributes FanoutPublisherSink = Attributes.CreateName("fanoutPublisherSink");
        /// <summary>
        /// Provides the default stage attribute named `ignoreSink`.
        /// </summary>
        public static readonly Attributes IgnoreSink = Attributes.CreateName("ignoreSink");
        /// <summary>
        /// Provides the default stage attribute named `actorRefSink`.
        /// </summary>
        public static readonly Attributes ActorRefSink = Attributes.CreateName("actorRefSink");
        /// <summary>
        /// Provides the default stage attribute named `actorRefWithAckSink`.
        /// </summary>
        public static readonly Attributes ActorRefWithAck = Attributes.CreateName("actorRefWithAckSink");
        /// <summary>
        /// Provides the default stage attribute named `actorSubscriberSink`.
        /// </summary>
        public static readonly Attributes ActorSubscriberSink = Attributes.CreateName("actorSubscriberSink");
        /// <summary>
        /// Provides the default stage attribute named `queueSink`.
        /// </summary>
        public static readonly Attributes QueueSink = Attributes.CreateName("queueSink");
        /// <summary>
        /// Provides the default stage attribute named `lazySink`.
        /// </summary>
        public static readonly Attributes LazySink = Attributes.CreateName("lazySink");
        /// <summary>
        /// Provides the default stage attribute named `lazyFlow`.
        /// </summary>
        public static readonly Attributes LazyFlow = Attributes.CreateName("lazyFlow");
        /// <summary>
        /// Provides the default stage attribute named `lazySource`.
        /// </summary>
        public static readonly Attributes LazySource = Attributes.CreateName("lazySource");
        /// <summary>
        /// Provides the `inputStreamSink` stage attribute together with the I/O dispatcher attribute.
        /// </summary>
        public static readonly Attributes InputStreamSink = Attributes.CreateName("inputStreamSink").And(IODispatcher);
        /// <summary>
        /// Provides the `outputStreamSink` stage attribute together with the I/O dispatcher attribute.
        /// </summary>
        public static readonly Attributes OutputStreamSink = Attributes.CreateName("outputStreamSink").And(IODispatcher);
        /// <summary>
        /// Provides the `fileSink` stage attribute together with the I/O dispatcher attribute.
        /// </summary>
        public static readonly Attributes FileSink = Attributes.CreateName("fileSink").And(IODispatcher);
        /// <summary>
        /// Provides the default stage attribute named `seqSink`.
        /// </summary>
        public static readonly Attributes SeqSink = Attributes.CreateName("seqSink");
        /// <summary>
        /// Provides the default stage attribute named `wireTap`.
        /// </summary>
        public static readonly Attributes WireTap = Attributes.CreateName("wireTap");
    }

    /// <summary>
    /// A graph stage backed by a symbolic stage definition that can be introspected before materialization.
    /// </summary>
    /// <typeparam name="TIn">The type accepted by the stage.</typeparam>
    /// <typeparam name="TOut">The type emitted by the stage.</typeparam>
    public sealed class SymbolicGraphStage<TIn, TOut> : PushPullGraphStage<TIn, TOut>
    {
        /// <summary>
        /// Creates a graph stage from a symbolic stage definition.
        /// </summary>
        /// <param name="symbolicStage">The symbolic definition used to create the stage and its attributes.</param>
        public SymbolicGraphStage(ISymbolicStage<TIn, TOut> symbolicStage) : base(symbolicStage.Create, symbolicStage.Attributes)
        {
        }
    }

    /// <summary>
    /// Describes a stream stage through its attributes and a factory for the effective stage implementation.
    /// </summary>
    /// <typeparam name="TIn">The type accepted by the stage.</typeparam>
    /// <typeparam name="TOut">The type emitted by the stage.</typeparam>
#pragma warning disable CS0618 // Type or member is obsolete
    public interface ISymbolicStage<in TIn, out TOut> : IStage<TIn, TOut>
#pragma warning restore CS0618 // Type or member is obsolete
    {
        /// <summary>
        /// Gets the attributes declared by this symbolic stage.
        /// </summary>
        Attributes Attributes { get; }

        /// <summary>
        /// Creates the concrete stage using the effective attributes supplied by materialization.
        /// </summary>
        /// <param name="effectiveAttributes">The effective attributes, including inherited and stage-local attributes.</param>
        /// <returns>The concrete stage implementation.</returns>
#pragma warning disable CS0618 // Type or member is obsolete
        IStage<TIn, TOut> Create(Attributes effectiveAttributes);
#pragma warning restore CS0618 // Type or member is obsolete
    }

    /// <summary>
    /// Base implementation of a symbolic stage that stores its declared attributes and supervision lookup.
    /// </summary>
    /// <typeparam name="TIn">The type accepted by the stage.</typeparam>
    /// <typeparam name="TOut">The type emitted by the stage.</typeparam>
    public abstract class SymbolicStage<TIn, TOut> : ISymbolicStage<TIn, TOut>
    {
        /// <summary>
        /// Creates a symbolic stage with the supplied declared attributes.
        /// </summary>
        /// <param name="attributes">The attributes declared by this stage.</param>
        protected SymbolicStage(Attributes attributes)
        {
            Attributes = attributes;
        }

        /// <summary>
        /// Gets the attributes declared by this symbolic stage.
        /// </summary>
        public Attributes Attributes { get; }

        /// <summary>
        /// Creates the concrete stage using the effective attributes supplied by materialization.
        /// </summary>
        /// <param name="effectiveAttributes">The effective attributes, including inherited and stage-local attributes.</param>
        /// <returns>The concrete stage implementation.</returns>
#pragma warning disable CS0618 // Type or member is obsolete
        public abstract IStage<TIn, TOut> Create(Attributes effectiveAttributes);
#pragma warning restore CS0618 // Type or member is obsolete

        /// <summary>
        /// Gets the supervision decider from the effective attributes, defaulting to stopping on failure.
        /// </summary>
        /// <param name="attributes">The effective attributes to inspect.</param>
        /// <returns>The configured supervision decider, or the stopping decider if none is configured.</returns>
        protected Decider Supervision(Attributes attributes)
            => attributes.GetAttribute(new ActorAttributes.SupervisionStrategy(Deciders.StoppingDecider)).Decider;
    }

    /// <summary>
    /// Sink stage that materializes a task with the first input element, returning the type's default for an empty stream unless configured to fault.
    /// </summary>
    /// <typeparam name="TIn">The input element type.</typeparam>
    public sealed class FirstOrDefault<TIn> : GraphStageWithMaterializedValue<SinkShape<TIn>, Task<TIn>>
    {
        #region internal classes
        
        private sealed class Logic : InGraphStageLogic
        {
            private readonly FirstOrDefault<TIn> _stage;
            private readonly TaskCompletionSource<TIn> _promise = TaskEx.NonBlockingTaskCompletionSource<TIn>();

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
        private readonly Inlet<TIn> _in = new("firstOrDefault.in");

        /// <summary>
        /// Creates a sink that returns the first element, optionally faulting if the stream is empty.
        /// </summary>
        /// <param name="throwOnDefault"><see langword="true"/> to fault the materialized task if the stream completes before producing an element.</param>
        public FirstOrDefault(bool throwOnDefault = false)
        {
            _throwOnDefault = throwOnDefault;
        }

        /// <summary>
        /// Gets the inlet shape of this sink.
        /// </summary>
        public override SinkShape<TIn> Shape => new(_in);

        /// <summary>
        /// Creates the stage logic and task materialized by this sink.
        /// </summary>
        /// <param name="inheritedAttributes">The attributes inherited by this stage.</param>
        /// <returns>The stage logic and its task-valued materialized value.</returns>
        public override ILogicAndMaterializedValue<Task<TIn>> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var logic = new Logic(this);
            return new LogicAndMaterializedValue<Task<TIn>>(logic, logic.Task);
        }

        /// <summary>
        /// Returns the diagnostic name of this sink stage.
        /// </summary>
        /// <returns>The string <c>FirstOrDefaultStage</c>.</returns>
        public override string ToString() => "FirstOrDefaultStage";
    }

    /// <summary>
    /// Sink stage that materializes a task with the last input element, returning the type's default for an empty stream unless configured to fault.
    /// </summary>
    /// <typeparam name="TIn">The input element type.</typeparam>
    public sealed class LastOrDefault<TIn> : GraphStageWithMaterializedValue<SinkShape<TIn>, Task<TIn>>
    {
        #region internal classes

        private sealed class Logic : InGraphStageLogic
        {
            private readonly LastOrDefault<TIn> _stage;
            private readonly TaskCompletionSource<TIn> _promise = TaskEx.NonBlockingTaskCompletionSource<TIn>();
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
        private readonly Inlet<TIn> _in = new("lastOrDefault.in");

        /// <summary>
        /// Creates a sink that returns the last element, optionally faulting if the stream is empty.
        /// </summary>
        /// <param name="throwOnDefault"><see langword="true"/> to fault the materialized task if the stream completes before producing an element.</param>
        public LastOrDefault(bool throwOnDefault = false)
        {
            _throwOnDefault = throwOnDefault;
        }

        /// <summary>
        /// Gets the inlet shape of this sink.
        /// </summary>
        public override SinkShape<TIn> Shape => new(_in);

        /// <summary>
        /// Creates the stage logic and task materialized by this sink.
        /// </summary>
        /// <param name="inheritedAttributes">The attributes inherited by this stage.</param>
        /// <returns>The stage logic and its task-valued materialized value.</returns>
        public override ILogicAndMaterializedValue<Task<TIn>> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var logic = new Logic(this);
            return new LogicAndMaterializedValue<Task<TIn>>(logic, logic.Task);
        }

        /// <summary>
        /// Returns the diagnostic name of this sink stage.
        /// </summary>
        /// <returns>The string <c>LastOrDefaultStage</c>.</returns>
        public override string ToString() => "LastOrDefaultStage";
    }
}
