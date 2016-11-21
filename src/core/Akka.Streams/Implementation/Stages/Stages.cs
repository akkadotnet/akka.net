//-----------------------------------------------------------------------
// <copyright file="Stages.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Event;
using Akka.Streams.Stage;
using Akka.Streams.Supervision;

namespace Akka.Streams.Implementation.Stages
{
    public static class DefaultAttributes
    {
        public static readonly Attributes IODispatcher = ActorAttributes.CreateDispatcher("akka.stream.default-blocking-io-dispatcher");

        public static readonly Attributes Fused = Attributes.CreateName("fused");
        public static readonly Attributes Select = Attributes.CreateName("select");
        public static readonly Attributes Log = Attributes.CreateName("log");
        public static readonly Attributes Where = Attributes.CreateName("where");
        public static readonly Attributes Collect = Attributes.CreateName("collect");
        public static readonly Attributes Sum = Attributes.CreateName("sum");
        public static readonly Attributes Recover = Attributes.CreateName("recover");
        public static readonly Attributes RecoverWith = Attributes.CreateName("recoverWith");
        public static readonly Attributes MapAsync = Attributes.CreateName("mapAsync");
        public static readonly Attributes MapAsyncUnordered = Attributes.CreateName("mapAsyncUnordered");
        public static readonly Attributes Grouped = Attributes.CreateName("grouped");
        public static readonly Attributes Limit = Attributes.CreateName("limit");
        public static readonly Attributes LimitWeighted = Attributes.CreateName("limitWeighted");
        public static readonly Attributes Sliding = Attributes.CreateName("sliding");
        public static readonly Attributes Take = Attributes.CreateName("take");
        public static readonly Attributes Drop = Attributes.CreateName("drop");
        public static readonly Attributes Skip = Attributes.CreateName("skip");
        public static readonly Attributes TakeWhile = Attributes.CreateName("takeWhile");
        public static readonly Attributes SkipWhile = Attributes.CreateName("skipWhile");
        public static readonly Attributes Scan = Attributes.CreateName("scan");
        public static readonly Attributes Aggregate = Attributes.CreateName("aggregate");
        public static readonly Attributes AggregateAsync = Attributes.CreateName("aggregateAsync");
        public static readonly Attributes Buffer = Attributes.CreateName("buffer");
        public static readonly Attributes Batch = Attributes.CreateName("batch");
        public static readonly Attributes BatchWeighted = Attributes.CreateName("batchWeighted");
        public static readonly Attributes Conflate = Attributes.CreateName("conflate");
        public static readonly Attributes Expand = Attributes.CreateName("expand");
        public static readonly Attributes StatefulSelectMany = Attributes.CreateName("statefulSelectMany");
        public static readonly Attributes GroupBy = Attributes.CreateName("groupBy");
        public static readonly Attributes PrefixAndTail = Attributes.CreateName("prefixAndTail");
        public static readonly Attributes Split = Attributes.CreateName("split");
        public static readonly Attributes ConcatAll = Attributes.CreateName("concatAll");
        public static readonly Attributes Processor = Attributes.CreateName("processor");
        public static readonly Attributes ProcessorWithKey = Attributes.CreateName("processorWithKey");
        public static readonly Attributes IdentityOp = Attributes.CreateName("identityOp");
        public static readonly Attributes DelimiterFraming = Attributes.CreateName("delimiterFraming");

        public static readonly Attributes Initial = Attributes.CreateName("initial");
        public static readonly Attributes Completion = Attributes.CreateName("completion");
        public static readonly Attributes Idle = Attributes.CreateName("idle");
        public static readonly Attributes IdleTimeoutBidi = Attributes.CreateName("idleTimeoutBidi");
        public static readonly Attributes DelayInitial = Attributes.CreateName("delayInitial");
        public static readonly Attributes IdleInject = Attributes.CreateName("idleInject");
        public static readonly Attributes BackpressureTimeout = Attributes.CreateName("backpressureTimeout");

        public static readonly Attributes Merge = Attributes.CreateName("merge");
        public static readonly Attributes MergePreferred = Attributes.CreateName("mergePreferred");
        public static readonly Attributes FlattenMerge = Attributes.CreateName("flattenMerge");
        public static readonly Attributes Broadcast = Attributes.CreateName("broadcast");
        public static readonly Attributes Balance = Attributes.CreateName("balance");
        public static readonly Attributes Zip = Attributes.CreateName("zip");
        public static readonly Attributes Unzip = Attributes.CreateName("unzip");
        public static readonly Attributes Concat = Attributes.CreateName("concat");
        public static readonly Attributes OrElse = Attributes.CreateName("orElse");
        public static readonly Attributes Repeat = Attributes.CreateName("repeat");
        public static readonly Attributes Unfold = Attributes.CreateName("unfold");
        public static readonly Attributes UnfoldAsync = Attributes.CreateName("unfoldAsync");
        public static readonly Attributes UnfoldInf = Attributes.CreateName("unfoldInf");
        public static readonly Attributes UnfoldResourceSource = Attributes.CreateName("unfoldResourceSource").And(IODispatcher);
        public static readonly Attributes UnfoldResourceSourceAsync = Attributes.CreateName("unfoldResourceSourceAsync").And(IODispatcher);
        public static readonly Attributes TerminationWatcher = Attributes.CreateName("terminationWatcher");
        public static readonly Attributes Delay = Attributes.CreateName("delay");
        public static readonly Attributes ZipN = Attributes.CreateName("zipN");
        public static readonly Attributes ZipWithN = Attributes.CreateName("zipWithN");

        public static readonly Attributes PublisherSource = Attributes.CreateName("publisherSource");
        public static readonly Attributes EnumerableSource = Attributes.CreateName("enumerableSource");
        public static readonly Attributes CycledSource = Attributes.CreateName("cycledSource");
        public static readonly Attributes TaskSource = Attributes.CreateName("taskSource");
        public static readonly Attributes TickSource = Attributes.CreateName("tickSource");
        public static readonly Attributes SingleSource = Attributes.CreateName("singleSource");
        public static readonly Attributes EmptySource = Attributes.CreateName("emptySource");
        public static readonly Attributes MaybeSource = Attributes.CreateName("maybeSource");
        public static readonly Attributes FailedSource = Attributes.CreateName("failedSource");
        public static readonly Attributes ConcatSource = Attributes.CreateName("concatSource");
        public static readonly Attributes ConcatMaterializedSource = Attributes.CreateName("concatMaterializedSource");
        public static readonly Attributes SubscriberSource = Attributes.CreateName("subscriberSource");
        public static readonly Attributes ActorPublisherSource = Attributes.CreateName("actorPublisherSource");
        public static readonly Attributes ActorRefSource = Attributes.CreateName("actorRefSource");
        public static readonly Attributes QueueSource = Attributes.CreateName("queueSource");
        public static readonly Attributes InputStreamSource = Attributes.CreateName("inputStreamSource").And(IODispatcher);
        public static readonly Attributes OutputStreamSource = Attributes.CreateName("outputStreamSource").And(IODispatcher);
        public static readonly Attributes FileSource = Attributes.CreateName("fileSource").And(IODispatcher);

        public static readonly Attributes SubscriberSink = Attributes.CreateName("subscriberSink");
        public static readonly Attributes CancelledSink = Attributes.CreateName("cancelledSink");
        public static readonly Attributes FirstSink = Attributes.CreateName("firstSink").And(Attributes.CreateInputBuffer(initial: 1, max: 1));
        public static readonly Attributes FirstOrDefaultSink = Attributes.CreateName("firstOrDefaultSink").And(Attributes.CreateInputBuffer(initial: 1, max: 1));
        public static readonly Attributes LastSink = Attributes.CreateName("lastSink");
        public static readonly Attributes LastOrDefaultSink = Attributes.CreateName("lastOrDefaultSink");
        public static readonly Attributes PublisherSink = Attributes.CreateName("publisherSink");
        public static readonly Attributes FanoutPublisherSink = Attributes.CreateName("fanoutPublisherSink");
        public static readonly Attributes IgnoreSink = Attributes.CreateName("ignoreSink");
        public static readonly Attributes ActorRefSink = Attributes.CreateName("actorRefSink");
        public static readonly Attributes ActorRefWithAck = Attributes.CreateName("actorRefWithAckSink");
        public static readonly Attributes ActorSubscriberSink = Attributes.CreateName("actorSubscriberSink");
        public static readonly Attributes QueueSink = Attributes.CreateName("queueSink");
        public static readonly Attributes LazySink = Attributes.CreateName("lazySink");
        public static readonly Attributes InputStreamSink = Attributes.CreateName("inputStreamSink").And(IODispatcher);
        public static readonly Attributes OutputStreamSink = Attributes.CreateName("outputStreamSink").And(IODispatcher);
        public static readonly Attributes FileSink = Attributes.CreateName("fileSink").And(IODispatcher);
        public static readonly Attributes SeqSink = Attributes.CreateName("seqSink");
    }

    /// <summary>
    /// Stage that is backed by a GraphStage but can be symbolically introspected
    /// </summary>
    public sealed class SymbolicGraphStage<TIn, TOut> : PushPullGraphStage<TIn, TOut>
    {
        public SymbolicGraphStage(ISymbolicStage<TIn, TOut> symbolicStage) : base(symbolicStage.Create, symbolicStage.Attributes)
        {
        }
    }

    public interface ISymbolicStage<in TIn, out TOut> : IStage<TIn, TOut>
    {
        Attributes Attributes { get; }

        IStage<TIn, TOut> Create(Attributes effectiveAttributes);
    }

    public abstract class SymbolicStage<TIn, TOut> : ISymbolicStage<TIn, TOut>
    {
        protected SymbolicStage(Attributes attributes)
        {
            Attributes = attributes;
        }

        public Attributes Attributes { get; }

        public abstract IStage<TIn, TOut> Create(Attributes effectiveAttributes);

        protected Decider Supervision(Attributes attributes)
            => attributes.GetAttribute(new ActorAttributes.SupervisionStrategy(Deciders.StoppingDecider)).Decider;
    }

    public sealed class Buffer<T> : SymbolicStage<T, T>
    {
        private readonly int _size;
        private readonly OverflowStrategy _overflowStrategy;

        public Buffer(int size, OverflowStrategy overflowStrategy, Attributes attributes = null) : base(attributes ?? DefaultAttributes.Buffer)
        {
            _size = size;
            _overflowStrategy = overflowStrategy;
        }

        public override IStage<T, T> Create(Attributes effectiveAttributes) => new Fusing.Buffer<T>(_size, _overflowStrategy);
    }

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

        public FirstOrDefault(bool throwOnDefault = false)
        {
            _throwOnDefault = throwOnDefault;
        }
        
        public override SinkShape<TIn> Shape => new SinkShape<TIn>(_in);

        public override ILogicAndMaterializedValue<Task<TIn>> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var logic = new Logic(this);
            return new LogicAndMaterializedValue<Task<TIn>>(logic, logic.Task);
        }

        public override string ToString() => "FirstOrDefaultStage";
    }

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

        public LastOrDefault(bool throwOnDefault = false)
        {
            _throwOnDefault = throwOnDefault;
        }

        public override SinkShape<TIn> Shape => new SinkShape<TIn>(_in);

        public override ILogicAndMaterializedValue<Task<TIn>> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var logic = new Logic(this);
            return new LogicAndMaterializedValue<Task<TIn>>(logic, logic.Task);
        }

        public override string ToString() => "LastOrDefaultStage";
    }
}