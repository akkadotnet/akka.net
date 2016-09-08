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
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using Akka.Streams.Supervision;
using Akka.Streams.Util;
using Reactive.Streams;

namespace Akka.Streams.Implementation.Stages
{
    internal static class DefaultAttributes
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

        public static readonly Attributes Merge = Attributes.CreateName("merge");
        public static readonly Attributes MergePreferred = Attributes.CreateName("mergePreferred");
        public static readonly Attributes FlattenMerge = Attributes.CreateName("flattenMerge");
        public static readonly Attributes Broadcast = Attributes.CreateName("broadcast");
        public static readonly Attributes Balance = Attributes.CreateName("balance");
        public static readonly Attributes Zip = Attributes.CreateName("zip");
        public static readonly Attributes Unzip = Attributes.CreateName("unzip");
        public static readonly Attributes Concat = Attributes.CreateName("concat");
        public static readonly Attributes Repeat = Attributes.CreateName("repeat");
        public static readonly Attributes Unfold = Attributes.CreateName("unfold");
        public static readonly Attributes UnfoldAsync = Attributes.CreateName("unfoldAsync");
        public static readonly Attributes UnfoldInf = Attributes.CreateName("unfoldInf");
        public static readonly Attributes UnfoldResourceSource = Attributes.CreateName("unfoldResourceSource").And(IODispatcher);
        public static readonly Attributes UnfoldResourceSourceAsync = Attributes.CreateName("unfoldResourceSourceAsync").And(IODispatcher);
        public static readonly Attributes TerminationWatcher = Attributes.CreateName("terminationWatcher");
        public static readonly Attributes Delay = Attributes.CreateName("delay").And(new Attributes.InputBuffer(16, 16));

        public static readonly Attributes PublisherSource = Attributes.CreateName("publisherSource");
        public static readonly Attributes EnumerableSource = Attributes.CreateName("enumerableSource");
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
        public static readonly Attributes InputStreamSink = Attributes.CreateName("inputStreamSink").And(IODispatcher);
        public static readonly Attributes OutputStreamSink = Attributes.CreateName("outputStreamSink").And(IODispatcher);
        public static readonly Attributes FileSink = Attributes.CreateName("fileSink").And(IODispatcher);
        public static readonly Attributes SeqSink = Attributes.CreateName("seqSink");
    }
    
    // FIXME: To be deprecated as soon as stream-of-stream operations are stages
    internal interface IStageModule
    {
        InPort In { get; }
        OutPort Out { get; }
    }

    internal abstract class StageModule<TIn, TOut> : FlowModule<TIn, TOut>, IStageModule
    {
        InPort IStageModule.In => In;
        OutPort IStageModule.Out => Out;
    }

    /// <summary>
    /// Stage that is backed by a GraphStage but can be symbolically introspected
    /// </summary>
    internal sealed class SymbolicGraphStage<TIn, TOut> : PushPullGraphStage<TIn, TOut>
    {
        public SymbolicGraphStage(ISymbolicStage<TIn, TOut> symbolicStage) : base(symbolicStage.Create, symbolicStage.Attributes)
        {
        }
    }

    internal interface ISymbolicStage<in TIn, out TOut> : IStage<TIn, TOut>
    {
        Attributes Attributes { get; }

        IStage<TIn, TOut> Create(Attributes effectiveAttributes);
    }

    internal abstract class SymbolicStage<TIn, TOut> : ISymbolicStage<TIn, TOut>
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

    internal sealed class Select<TIn, TOut> : SymbolicStage<TIn, TOut>
    {
        private readonly Func<TIn, TOut> _mapper;

        public Select(Func<TIn, TOut> mapper, Attributes attributes = null) : base(attributes ?? DefaultAttributes.Select)
        {
            _mapper = mapper;
        }

        public override IStage<TIn, TOut> Create(Attributes effectiveAttributes)
            => new Fusing.Select<TIn, TOut>(_mapper, Supervision(effectiveAttributes));
    }

    internal sealed class Log<T> : SymbolicStage<T, T>
    {
        private readonly string _name;
        private readonly Func<T, object> _extract;
        private readonly ILoggingAdapter _loggingAdapter;

        public Log(string name, Func<T, object> extract, ILoggingAdapter loggingAdapter, Attributes attributes = null) : base(attributes ?? DefaultAttributes.Log)
        {
            _name = name;
            _extract = extract;
            _loggingAdapter = loggingAdapter;
        }

        public override IStage<T, T> Create(Attributes effectiveAttributes)
            => new Fusing.Log<T>(_name, _extract, _loggingAdapter, Supervision(effectiveAttributes));
    }

    internal sealed class Where<T> : SymbolicStage<T, T>
    {
        private readonly Predicate<T> _predicate;

        public Where(Predicate<T> predicate, Attributes attributes = null) : base(attributes ?? DefaultAttributes.Where)
        {
            _predicate = predicate;
        }

        public override IStage<T, T> Create(Attributes effectiveAttributes)
            => new Fusing.Where<T>(_predicate, Supervision(effectiveAttributes));
    }

    internal sealed class Recover<T> : SymbolicStage<T, Option<T>>
    {
        private readonly Func<Exception, Option<T>> _func;

        public Recover(Func<Exception, Option<T>> func, Attributes attributes = null) : base(attributes ?? DefaultAttributes.Recover)
        {
            _func = func;
        }

        public override IStage<T, Option<T>> Create(Attributes effectiveAttributes) => new Fusing.Recover<T>(_func);
    }

    internal sealed class Grouped<T> : SymbolicStage<T, IEnumerable<T>>
    {
        private readonly int _count;

        public Grouped(int count, Attributes attributes = null) : base(attributes ?? DefaultAttributes.Grouped)
        {
            if (count <= 0) throw new ArgumentException("Grouped count must be greater than 0", nameof(count));
            _count = count;
        }

        public override IStage<T, IEnumerable<T>> Create(Attributes effectiveAttributes) => new Fusing.Grouped<T>(_count);
    }

    internal sealed class Sliding<T> : SymbolicStage<T, IEnumerable<T>>
    {
        private readonly int _count;
        private readonly int _step;

        public Sliding(int count, int step, Attributes attributes = null) : base(attributes ?? DefaultAttributes.Sliding)
        {
            if (count <= 0) throw new ArgumentException("Sliding count must be greater than 0", nameof(count));
            if (step <= 0) throw new ArgumentException("Sliding step must be greater than 0", nameof(step));
            _count = count;
            _step = step;
        }

        public override IStage<T, IEnumerable<T>> Create(Attributes effectiveAttributes) => new Fusing.Sliding<T>(_count, _step);
    }

    internal sealed class Scan<TIn, TOut> : SymbolicStage<TIn, TOut>
    {
        private readonly TOut _zero;
        private readonly Func<TOut, TIn, TOut> _aggregate;

        public Scan(TOut zero, Func<TOut, TIn, TOut> aggregate, Attributes attributes = null) : base(attributes ?? DefaultAttributes.Scan)
        {
            _zero = zero;
            _aggregate = aggregate;
        }

        public override IStage<TIn, TOut> Create(Attributes effectiveAttributes)
            => new Fusing.Scan<TIn, TOut>(_zero, _aggregate, Supervision(effectiveAttributes));
    }

    internal sealed class Aggregate<TIn, TOut> : SymbolicStage<TIn, TOut>
    {
        private readonly TOut _zero;
        private readonly Func<TOut, TIn, TOut> _aggregate;

        public Aggregate(TOut zero, Func<TOut, TIn, TOut> aggregate, Attributes attributes = null) : base(attributes ?? DefaultAttributes.Aggregate)
        {
            _zero = zero;
            _aggregate = aggregate;
        }

        public override IStage<TIn, TOut> Create(Attributes effectiveAttributes)
            => new Fusing.Aggregate<TIn, TOut>(_zero, _aggregate, Supervision(effectiveAttributes));
    }

    internal sealed class Buffer<T> : SymbolicStage<T, T>
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

    internal sealed class FirstOrDefault<TIn> : GraphStageWithMaterializedValue<SinkShape<TIn>, Task<TIn>>
    {
        #region internal classes
        
        private sealed class Logic : GraphStageLogic
        {
            private readonly Inlet<TIn> _inlet;
            private readonly TaskCompletionSource<TIn> _promise = new TaskCompletionSource<TIn>();

            public Task<TIn> Task => _promise.Task;

            public Logic(FirstOrDefault<TIn> stage) : base(stage.Shape)
            {
                _inlet = stage._in;

                Action onPush = () =>
                {
                    _promise.TrySetResult(Grab(_inlet));
                    CompleteStage();
                };

                Action onUpstreamFinish = () =>
                {
                    if (stage._throwOnDefault)
                        _promise.TrySetException(new NoSuchElementException("First of empty stream"));
                    else
                        _promise.TrySetResult(default(TIn));

                    CompleteStage();
                };

                Action<Exception> onUpstreamFailure = e =>
                {
                    _promise.TrySetException(e);
                    FailStage(e);
                };

                SetHandler(stage._in, onPush, onUpstreamFinish, onUpstreamFailure);
            }

            public override void PreStart() => Pull(_inlet);
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

    internal sealed class LastOrDefault<TIn> : GraphStageWithMaterializedValue<SinkShape<TIn>, Task<TIn>>
    {
        #region internal classes

        private sealed class Logic : GraphStageLogic
        {
            private readonly Inlet<TIn> _inlet;
            private readonly TaskCompletionSource<TIn> _promise = new TaskCompletionSource<TIn>();
            private TIn _prev;
            private bool _foundAtLeastOne;

            public Task<TIn> Task => _promise.Task;

            public Logic(LastOrDefault<TIn> stage) : base(stage.Shape)
            {
                _inlet = stage._in;

                Action onPush = () =>
                {
                    _prev = Grab(_inlet);
                    _foundAtLeastOne = true;
                    Pull(_inlet);
                };

                Action onUpstreamFinish = () =>
                {
                    if (stage._throwOnDefault && !_foundAtLeastOne)
                        _promise.TrySetException(new NoSuchElementException("Last of empty stream"));
                    else
                        _promise.TrySetResult(_prev);

                    CompleteStage();
                };

                Action<Exception> onUpstreamFailure = e =>
                {
                    _promise.TrySetException(e);
                    FailStage(e);
                };

                SetHandler(stage._in, onPush, onUpstreamFinish, onUpstreamFailure);
            }

            public override void PreStart() => Pull(_inlet);
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

    // FIXME: These are not yet proper stages, therefore they use the deprecated StageModule infrastructure

    internal interface IGroupBy
    {
        int MaxSubstreams { get; }
        Func<object, object> Extractor { get; }
    }

    internal sealed class GroupBy<TIn, TKey> : StageModule<TIn, Source<TIn, NotUsed>>, IGroupBy
    {
        private readonly Func<TIn, TKey> _extractor;
        private readonly Func<object, object> _extractorWrapper;

        public GroupBy(int maxSubstreams, Func<TIn, TKey> extractor, Attributes attributes = null)
        {
            MaxSubstreams = maxSubstreams;
            _extractor = extractor;
            _extractorWrapper = _ => _extractor((TIn) _);
            Attributes = attributes ?? DefaultAttributes.GroupBy;

            Label = $"GroupBy({maxSubstreams})";
        }

        public int MaxSubstreams { get; }

        public Func<TIn, TKey> Extractor => _extractor;

        Func<object, object> IGroupBy.Extractor => _extractorWrapper;

        public override IModule CarbonCopy() => new GroupBy<TIn, TKey>(MaxSubstreams, Extractor, Attributes);

        public override Attributes Attributes { get; }

        public override IModule WithAttributes(Attributes attributes)
            => new GroupBy<TIn, TKey>(MaxSubstreams, Extractor, attributes);

        protected override string Label { get; }
    }

    internal sealed class DirectProcessor<TIn, TOut> : StageModule<TIn, TOut>
    {
        public readonly Func<Tuple<IProcessor<TIn, TOut>, object>> ProcessorFactory;

        public DirectProcessor(Func<Tuple<IProcessor<TIn, TOut>, object>> processorFactory, Attributes attributes = null)
        {
            ProcessorFactory = processorFactory;
            Attributes = attributes ?? DefaultAttributes.Processor;
        }

        public override IModule CarbonCopy() => new DirectProcessor<TIn, TOut>(ProcessorFactory, Attributes);

        public override Attributes Attributes { get; }

        public override IModule WithAttributes(Attributes attributes)
            => new DirectProcessor<TIn, TOut>(ProcessorFactory, attributes);
    }
}