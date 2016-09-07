//-----------------------------------------------------------------------
// <copyright file="Ops.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Event;
using Akka.Pattern;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation.Stages;
using Akka.Streams.Stage;
using Akka.Streams.Supervision;
using Akka.Streams.Util;
using Akka.Util;

namespace Akka.Streams.Implementation.Fusing
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class Select<TIn, TOut> : PushStage<TIn, TOut>
    {
        private readonly Func<TIn, TOut> _func;
        private readonly Decider _decider;

        public Select(Func<TIn, TOut> func, Decider decider)
        {
            _func = func;
            _decider = decider;
        }

        public override ISyncDirective OnPush(TIn element, IContext<TOut> context) => context.Push(_func(element));

        public override Directive Decide(Exception cause) => _decider(cause);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class Where<T> : PushStage<T, T>
    {
        private readonly Predicate<T> _predicate;
        private readonly Decider _decider;

        public Where(Predicate<T> predicate, Decider decider)
        {
            _predicate = predicate;
            _decider = decider;
        }

        public override ISyncDirective OnPush(T element, IContext<T> context)
            => _predicate(element) ? (ISyncDirective) context.Push(element) : context.Pull();

        public override Directive Decide(Exception cause) => _decider(cause);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class TakeWhile<T> : SimpleLinearGraphStage<T>
    {
        #region Logic

        private sealed class Logic : GraphStageLogic
        {
            public Logic(TakeWhile<T> take, Attributes inheritedAttributes) : base(take.Shape)
            {
                var attr = inheritedAttributes.GetAttribute<ActorAttributes.SupervisionStrategy>(null);
                var decider = attr != null ? attr.Decider : Deciders.StoppingDecider;

                SetHandler(take.Outlet, onPull: () => Pull(take.Inlet));
                SetHandler(take.Inlet, onPush: () =>
                {
                    try
                    {
                        var element = Grab(take.Inlet);
                        if(take._predicate(element))
                            Push(take.Outlet, element);
                        else
                            CompleteStage();
                    }
                    catch (Exception ex)
                    {
                        if(decider(ex) == Directive.Stop)
                            FailStage(ex);
                        else
                            Pull(take.Inlet);
                    }
                });
            }

            public override string ToString() => "TakeWhileLogic";
        }

        #endregion

        private readonly Predicate<T> _predicate;

        public TakeWhile(Predicate<T> predicate)
        {
            _predicate = predicate;
        }

        protected override Attributes InitialAttributes { get; } = DefaultAttributes.TakeWhile;

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            => new Logic(this, inheritedAttributes);

        public override string ToString() => "TakeWhile";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class SkipWhile<T> : GraphStage<FlowShape<T,T>>
    {
        #region Logic

        private sealed class Logic : SupervisedGraphStageLogic
        {
            private readonly SkipWhile<T> _skip;

            public Logic(SkipWhile<T> skip, Attributes inheritedAttributes) : base(inheritedAttributes, skip.Shape)
            {
                _skip = skip;

                SetHandler(skip.In, onPush: () =>
                {
                    var element = Grab(skip.In);
                    var result = WithSupervision(() => skip._predicate(element));
                    if (result.HasValue)
                    {
                        if (result.Value)
                            Pull(skip.In);
                        else
                        {
                            Push(skip.Out, element);
                            SetHandler(skip.In, onPush: () => Push(skip.Out, Grab(skip.In)));
                        }
                    }
                });

                SetHandler(skip.Out, onPull: () => Pull(skip.In));
            }

            protected override void OnResume(Exception ex)
            {
                if(!HasBeenPulled(_skip.In))
                    Pull(_skip.In);
            }
        }

        #endregion

        private readonly Predicate<T> _predicate;

        public SkipWhile(Predicate<T> predicate)
        {
            _predicate = predicate;
            Shape = new FlowShape<T, T>(In, Out);
        }

        protected override Attributes InitialAttributes { get; } = DefaultAttributes.SkipWhile;

        public Inlet<T> In { get; } = new Inlet<T>("SkipWhile.in");

        public Outlet<T> Out { get; } = new Outlet<T>("SkipWhile.out");

        public override FlowShape<T, T> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            => new Logic(this, inheritedAttributes);

        public override string ToString() => "SkipWhile";
    }
    
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal abstract class SupervisedGraphStageLogic : GraphStageLogic
    {
        private readonly Lazy<Decider> _decider;

        protected SupervisedGraphStageLogic(Attributes inheritedAttributes, Shape shape) : base(shape)
        {
            _decider = new Lazy<Decider>(() =>
            {
                var attr = inheritedAttributes.GetAttribute<ActorAttributes.SupervisionStrategy>(null);
                return attr != null ? attr.Decider : Deciders.StoppingDecider;
            });
        }

        protected Option<T> WithSupervision<T>(Func<T> function)
        {
            try
            {
                return function();
            }
            catch (Exception ex)
            {
                switch (_decider.Value(ex))
                {
                    case Directive.Stop:
                        OnStop(ex);
                        break;
                    case Directive.Resume:
                        OnResume(ex);
                        break;
                    case Directive.Restart:
                        OnRestart(ex);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
                return Option<T>.None;
            }
        }

        protected virtual void OnRestart(Exception ex) => OnResume(ex);

        protected virtual void OnResume(Exception ex)
        {
        }

        protected virtual void OnStop(Exception ex) => FailStage(ex);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class Collect<TIn, TOut> : GraphStage<FlowShape<TIn, TOut>>
    {
        #region Logic

        private sealed class Logic : SupervisedGraphStageLogic
        {
            private readonly Collect<TIn, TOut> _collect;

            public Logic(Collect<TIn, TOut> collect, Attributes inheritedAttributes)
                : base(inheritedAttributes, collect.Shape)
            {
                _collect = collect;

                SetHandler(collect.In, onPush: () =>
                {
                    var result = WithSupervision(() => collect._func(Grab(collect.In)));
                    if (result.HasValue)
                    {
                        if(result.Value.IsDefaultForType())
                            Pull(collect.In);
                        else
                            Push(collect.Out, result.Value);
                    }
                });

                SetHandler(collect.Out, onPull: ()=> Pull(collect.In));
            }

            protected override void OnResume(Exception ex)
            {
                if(!HasBeenPulled(_collect.In))
                    Pull(_collect.In);
            }
        }

        #endregion

        private readonly Func<TIn, TOut> _func;

        public Collect(Func<TIn, TOut> func)
        {
            _func = func;
            Shape = new FlowShape<TIn, TOut>(In, Out);
        }

        protected override Attributes InitialAttributes { get; } = DefaultAttributes.Collect;

        public Inlet<TIn> In { get; } = new Inlet<TIn>("Collect.in");

        public Outlet<TOut> Out { get; } = new Outlet<TOut>("Collect.out");

        public override FlowShape<TIn, TOut> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            => new Logic(this, inheritedAttributes);

        public override string ToString() => "Collect";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class Recover<T> : PushPullStage<T, Option<T>>
    {
        private readonly Func<Exception, Option<T>> _recovery;
        private Option<T> _recovered;

        public Recover(Func<Exception, Option<T>> recovery)
        {
            _recovery = recovery;
        }

        public override ISyncDirective OnPush(T element, IContext<Option<T>> context) => context.Push(element);

        public override ISyncDirective OnPull(IContext<Option<T>> context) => _recovered.HasValue ? (ISyncDirective) context.PushAndFinish(_recovered) : context.Pull();

        public override ITerminationDirective OnUpstreamFailure(Exception cause, IContext<Option<T>> context)
        {
            var result = _recovery(cause);
            if (!result.HasValue)
                return context.Fail(cause);

            _recovered = result;
            return context.AbsorbTermination();
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class Take<T> : SimpleLinearGraphStage<T>
    {
        #region Logic

        private sealed class Logic : GraphStageLogic
        {
            public Logic(Take<T> take) : base(take.Shape)
            {
                var left = take._count;

                SetHandler(take.Outlet, onPull: () =>
                {
                    if (left > 0)
                        Pull(take.Inlet);
                    else
                        CompleteStage();
                });

                SetHandler(take.Inlet, onPush: () =>
                {
                    var leftBefore = left;
                    if (leftBefore >= 1)
                    {
                        left = leftBefore - 1;
                        Push(take.Outlet, Grab(take.Inlet));
                    }

                    if (leftBefore <= 1)
                        CompleteStage();
                });
            }
        }

        #endregion

        private readonly long _count;

        public Take(long count)
        {
            _count = count;
        }

        protected override Attributes InitialAttributes { get; } = DefaultAttributes.Take;

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        public override string ToString() => "Take";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class Drop<T> : SimpleLinearGraphStage<T>
    {
        #region Logic

        private sealed class Logic : GraphStageLogic
        {
            public Logic(Drop<T> drop) : base(drop.Shape)
            {
                var left = drop._count;

                SetHandler(drop.Inlet, onPush: () =>
                {
                    if (left > 0)
                    {
                        left--;
                        Pull(drop.Inlet);
                    }
                    else
                        Push(drop.Outlet, Grab(drop.Inlet));
                });

                SetHandler(drop.Outlet, onPull: () => Pull(drop.Inlet));
            }
        }

        #endregion

        private readonly long _count;

        public Drop(long count)
        {
            _count = count;
        }

        protected override Attributes InitialAttributes { get; } = DefaultAttributes.Drop;

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        public override string ToString() => "Drop";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class Scan<TIn, TOut> : PushPullStage<TIn, TOut>
    {
        private readonly TOut _zero;
        private readonly Func<TOut, TIn, TOut> _aggregate;
        private readonly Decider _decider;
        private TOut _aggregator;
        private bool _pushedZero;

        public Scan(TOut zero, Func<TOut, TIn, TOut> aggregate, Decider decider)
        {
            _zero = _aggregator = zero;
            _aggregate = aggregate;
            _decider = decider;
        }

        public override ISyncDirective OnPush(TIn element, IContext<TOut> context)
        {
            if (_pushedZero)
            {
                _aggregator = _aggregate(_aggregator, element);
                return context.Push(_aggregator);
            }

            _aggregator = _aggregate(_zero, element);
            return context.Push(_zero);
        }

        public override ISyncDirective OnPull(IContext<TOut> context)
        {
            if (!_pushedZero)
            {
                _pushedZero = true;
                return context.IsFinishing ? context.PushAndFinish(_aggregator) : context.Push(_aggregator);
            }

            return context.Pull();
        }

        public override ITerminationDirective OnUpstreamFinish(IContext<TOut> context) => _pushedZero ? context.Finish() : context.AbsorbTermination();

        public override Directive Decide(Exception cause) => _decider(cause);

        public override IStage<TIn, TOut> Restart() => new Scan<TIn, TOut>(_zero, _aggregate, _decider);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class Aggregate<TIn, TOut> : PushPullStage<TIn, TOut>
    {
        private readonly TOut _zero;
        private readonly Func<TOut, TIn, TOut> _aggregate;
        private readonly Decider _decider;
        private TOut _aggregator;

        public Aggregate(TOut zero, Func<TOut, TIn, TOut> aggregate, Decider decider)
        {
            _zero = _aggregator = zero;
            _aggregate = aggregate;
            _decider = decider;
        }

        public override ISyncDirective OnPush(TIn element, IContext<TOut> context)
        {
            _aggregator = _aggregate(_aggregator, element);
            return context.Pull();
        }

        public override ISyncDirective OnPull(IContext<TOut> context) => context.IsFinishing ? (ISyncDirective) context.PushAndFinish(_aggregator) : context.Pull();

        public override ITerminationDirective OnUpstreamFinish(IContext<TOut> context) => context.AbsorbTermination();

        public override Directive Decide(Exception cause) => _decider(cause);

        public override IStage<TIn, TOut> Restart() => new Aggregate<TIn, TOut>(_zero, _aggregate, _decider);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class Intersperse<T> : GraphStage<FlowShape<T, T>>
    {
        #region internal class

        private sealed class StartInHandler : InHandler
        {
            private readonly Intersperse<T> _stage;
            private readonly Logic _logic;

            public StartInHandler(Intersperse<T> stage, Logic logic)
            {
                _stage = stage;
                _logic = logic;
            }

            public override void OnPush()
            {
                // if else (to avoid using Iterator[T].flatten in hot code)
                if (_stage.InjectStartEnd) _logic.EmitMultiple(_stage.Out, new[] {_stage._start, _logic.Grab(_stage.In)});
                else _logic.Emit(_stage.Out, _logic.Grab(_stage.In));
                _logic.SetHandler(_stage.In, new RestInHandler(_stage, _logic));
            }

            public override void OnUpstreamFinish()
            {
                _logic.EmitMultiple(_stage.Out, new[] {_stage._start, _stage._end});
                _logic.CompleteStage();
            }
        }

        private sealed class RestInHandler : InHandler
        {
            private readonly Intersperse<T> _stage;
            private readonly Logic _logic;

            public RestInHandler(Intersperse<T> stage, Logic logic)
            {
                _stage = stage;
                _logic = logic;
            }

            public override void OnPush() => _logic.EmitMultiple(_stage.Out, new[] {_stage._inject, _logic.Grab(_stage.In)});

            public override void OnUpstreamFinish()
            {
                if (_stage.InjectStartEnd) _logic.Emit(_stage.Out, _stage._end);
                _logic.CompleteStage();
            }
        }

        private sealed class Logic : GraphStageLogic
        {
            public Logic(Intersperse<T> stage) : base(stage.Shape)
            {
                SetHandler(stage.In, new StartInHandler(stage, this));
                SetHandler(stage.Out, onPull: () => Pull(stage.In));
            }
        }

        #endregion

        public readonly Inlet<T> In = new Inlet<T>("in");
        public readonly Outlet<T> Out = new Outlet<T>("out");
        private readonly T _start;
        private readonly T _inject;
        private readonly T _end;

        public Intersperse(T inject)
        {
            _inject = inject;
            InjectStartEnd = false;

            Shape = new FlowShape<T, T>(In, Out);
        }

        public Intersperse(T start, T inject, T end)
        {
            _start = start;
            _inject = inject;
            _end = end;
            InjectStartEnd = true;

            Shape = new FlowShape<T, T>(In, Out);
        }

        public bool InjectStartEnd { get; }

        public override FlowShape<T, T> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class Grouped<T> : PushPullStage<T, IEnumerable<T>>
    {
        private readonly int _count;
        private List<T> _buffer;
        private int _left;

        public Grouped(int count)
        {
            _count = _left = count;
            _buffer = new List<T>(_count);
        }

        public override ISyncDirective OnPush(T element, IContext<IEnumerable<T>> context)
        {
            _buffer.Add(element);
            _left--;
            if (_left == 0)
            {
                var result = _buffer;
                _buffer = new List<T>(_count);
                _left = _count;
                return context.Push(result);
            }
            return context.Pull();
        }

        public override ISyncDirective OnPull(IContext<IEnumerable<T>> context)
        {
            if (context.IsFinishing)
            {
                var result = _buffer;
                _left = _count;
                _buffer = new List<T>(_count);
                return context.PushAndFinish(result);
            }
            return context.Pull();
        }

        public override ITerminationDirective OnUpstreamFinish(IContext<IEnumerable<T>> context) => _left == _count ? context.Finish() : context.AbsorbTermination();
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class LimitWeighted<T> : GraphStage<FlowShape<T, T>>
    {
        #region Logic

        private sealed class Logic : SupervisedGraphStageLogic
        {
            private readonly LimitWeighted<T> _limit;
            private long _left;

            public Logic(LimitWeighted<T> limit, Attributes inheritedAttributes)
                : base(inheritedAttributes, limit.Shape)
            {
                _limit = limit;
                _left = limit._max;

                SetHandler(limit.In, onPush: () =>
                {
                    var element = Grab(limit.In);
                    var result = WithSupervision(() => limit._costFunc(element));
                    if (result.HasValue)
                    {
                        _left -= result.Value;
                        if(_left >= 0)
                            Push(limit.Out, element);
                        else
                            FailStage(new StreamLimitReachedException(limit._max));
                    }
                });

                SetHandler(limit.Out, onPull: () => Pull(limit.In));
            }

            protected override void OnResume(Exception ex) => TryPull();

            protected override void OnRestart(Exception ex)
            {
                _left = _limit._max;
                TryPull();
            }

            private void TryPull()
            {
                if (!HasBeenPulled(_limit.In))
                    Pull(_limit.In);
            }
        }

        #endregion

        private readonly long _max;
        private readonly Func<T, long> _costFunc;

        public LimitWeighted(long max, Func<T, long> costFunc)
        {
            _max = max;
            _costFunc = costFunc;
            Shape = new FlowShape<T, T>(In, Out);
        }

        protected override Attributes InitialAttributes { get; } = DefaultAttributes.LimitWeighted;

        public Inlet<T> In { get; } = new Inlet<T>("LimitWeighted.in");

        public Outlet<T> Out { get; } = new Outlet<T>("LimitWeighted.out");

        public override FlowShape<T, T> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            => new Logic(this, inheritedAttributes);

        public override string ToString() => "LimitWeighted";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class Sliding<T> : PushPullStage<T, IEnumerable<T>>
    {
        private readonly int _count;
        private readonly int _step;
        private IImmutableList<T> _buffer;

        public Sliding(int count, int step)
        {
            _count = count;
            _step = step;
            _buffer = ImmutableList<T>.Empty;
        }

        public override ISyncDirective OnPush(T element, IContext<IEnumerable<T>> context)
        {
            _buffer = _buffer.Add(element);

            if (_buffer.Count < _count)
                return context.Pull();
            if (_buffer.Count == _count)
                return context.Push(_buffer);
            if (_step > _count)
            {
                if (_buffer.Count == _step)
                    _buffer = ImmutableList<T>.Empty;
                return context.Pull();
            }

            _buffer = _buffer.Skip(_step).ToImmutableList();
            return _buffer.Count == _count ? (ISyncDirective) context.Push(_buffer) : context.Pull();
        }

        public override ISyncDirective OnPull(IContext<IEnumerable<T>> context)
        {
            if (!context.IsFinishing)
                return context.Pull();
            if (_buffer.Count >= _count)
                return context.Finish();

            return context.PushAndFinish(_buffer);
        }

        public override ITerminationDirective OnUpstreamFinish(IContext<IEnumerable<T>> context) => _buffer.Count == 0 ? context.Finish() : context.AbsorbTermination();
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class Buffer<T> : DetachedStage<T, T>
    {
        private readonly int _count;
        private readonly Func<IDetachedContext<T>, T, IUpstreamDirective> _enqueueAction;
        private IBuffer<T> _buffer;

        public Buffer(int count, OverflowStrategy overflowStrategy)
        {
            _count = count;
            _enqueueAction = EnqueueAction(overflowStrategy);
        }

        public override void PreStart(ILifecycleContext context) => _buffer = Buffer.Create<T>(_count, context.Materializer);

        public override IUpstreamDirective OnPush(T element, IDetachedContext<T> context) => context.IsHoldingDownstream ? context.PushAndPull(element) : _enqueueAction(context, element);

        public override IDownstreamDirective OnPull(IDetachedContext<T> context)
        {
            if (context.IsFinishing)
            {
                var element = _buffer.Dequeue();
                return _buffer.IsEmpty ? context.PushAndFinish(element) : context.Push(element);
            }
            if (context.IsHoldingUpstream)
                return context.PushAndPull(_buffer.Dequeue());
            if (_buffer.IsEmpty)
                return context.HoldDownstream();
            return context.Push(_buffer.Dequeue());
        }

        public override ITerminationDirective OnUpstreamFinish(IDetachedContext<T> context) => _buffer.IsEmpty ? context.Finish() : context.AbsorbTermination();

        private Func<IDetachedContext<T>, T, IUpstreamDirective> EnqueueAction(OverflowStrategy overflowStrategy)
        {
            switch (overflowStrategy)
            {
                case OverflowStrategy.DropHead:
                    return (context, element) =>
                    {
                        if (_buffer.IsFull)
                            _buffer.DropHead();
                        _buffer.Enqueue(element);
                        return context.Pull();
                    };
                case OverflowStrategy.DropTail:
                    return (context, element) =>
                    {
                        if (_buffer.IsFull)
                            _buffer.DropTail();
                        _buffer.Enqueue(element);
                        return context.Pull();
                    };
                case OverflowStrategy.DropBuffer:
                    return (context, element) =>
                    {
                        if (_buffer.IsFull)
                            _buffer.Clear();
                        _buffer.Enqueue(element);
                        return context.Pull();
                    };
                case OverflowStrategy.DropNew:
                    return (context, element) =>
                    {
                        if (!_buffer.IsFull)
                            _buffer.Enqueue(element);
                        return context.Pull();
                    };
                case OverflowStrategy.Backpressure:
                    return (context, element) =>
                    {
                        _buffer.Enqueue(element);
                        return _buffer.IsFull ? context.HoldUpstream() : context.Pull();
                    };
                case OverflowStrategy.Fail:
                    return (context, element) =>
                    {
                        if (_buffer.IsFull)
                            return context.Fail(new BufferOverflowException($"Buffer overflow (max capacity was {_count})"));
                        _buffer.Enqueue(element);
                        return context.Pull();
                    };
                default:
                    throw new NotSupportedException($"Overflow strategy {overflowStrategy} is not supported");
            }
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class Completed<T> : PushPullStage<T, T>
    {
        public override ISyncDirective OnPush(T element, IContext<T> context) => context.Finish();

        public override ISyncDirective OnPull(IContext<T> context) => context.Finish();
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class OnCompleted<TIn, TOut> : PushStage<TIn, TOut>
    {
        private readonly Action _success;
        private readonly Action<Exception> _failure;

        public OnCompleted(Action success, Action<Exception> failure)
        {
            _success = success;
            _failure = failure;
        }

        public override ISyncDirective OnPush(TIn element, IContext<TOut> context) => context.Pull();

        public override ITerminationDirective OnUpstreamFailure(Exception cause, IContext<TOut> context)
        {
            _failure(cause);
            return context.Fail(cause);
        }

        public override ITerminationDirective OnUpstreamFinish(IContext<TOut> context)
        {
            _success();
            return context.Finish();
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class Batch<TIn, TOut> : GraphStage<FlowShape<TIn, TOut>>
    {
        #region internal classes

        private sealed class Logic : GraphStageLogic
        {
            private readonly FlowShape<TIn, TOut> _shape;
            private readonly Batch<TIn, TOut> _stage;
            private readonly Decider _decider;
            private Option<TOut> _aggregate;
            private long _left;
            private Option<TIn> _pending;

            public Logic(Attributes inheritedAttributes, Batch<TIn, TOut> stage) : base(stage.Shape)
            {
                _shape = stage.Shape;
                _stage = stage;
                var attr = inheritedAttributes.GetAttribute<ActorAttributes.SupervisionStrategy>(null);
                _decider = attr != null ? attr.Decider : Deciders.StoppingDecider;
                _left = stage._max;

                SetHandler(_shape.Inlet, onPush: () =>
                {
                    var element = Grab(_shape.Inlet);
                    var cost = _stage._costFunc(element);
                    if (!_aggregate.HasValue)
                    {
                        try
                        {
                            _aggregate = _stage._seed(element);
                            _left -= cost;
                        }
                        catch (Exception ex)
                        {
                            switch (_decider(ex))
                            {
                                case Directive.Stop:
                                    FailStage(ex);
                                    break;
                                case Directive.Restart:
                                    RestartState();
                                    break;
                                case Directive.Resume:
                                    break;
                            }
                        }
                    }
                    else if (_left < cost)
                        _pending = element;
                    else
                    {
                        try
                        {
                            _aggregate = _stage._aggregate(_aggregate.Value, element);
                            _left -= cost;
                        }
                        catch (Exception ex)
                        {
                            switch (_decider(ex))
                            {
                                case Directive.Stop:
                                    FailStage(ex);
                                    break;
                                case Directive.Restart:
                                    RestartState();
                                    break;
                                case Directive.Resume:
                                    break;
                            }
                        }
                    }

                    if (IsAvailable(_shape.Outlet))
                        Flush();
                    if (!_pending.HasValue)
                        Pull(_shape.Inlet);
                }, onUpstreamFinish: () =>
                {
                    if (!_aggregate.HasValue)
                        CompleteStage();
                });

                SetHandler(_shape.Outlet, onPull: () =>
                {
                    if (!_aggregate.HasValue)
                    {
                        if (IsClosed(_shape.Inlet))
                            CompleteStage();
                        else if (!HasBeenPulled(_shape.Inlet))
                            Pull(_shape.Inlet);
                    }
                    else if (IsClosed(_shape.Inlet))
                    {
                        Push(_shape.Outlet, _aggregate.Value);
                        if (!_pending.HasValue)
                            CompleteStage();
                        else
                        {
                            try
                            {
                                _aggregate = _stage._seed(_pending.Value);
                            }
                            catch (Exception ex)
                            {
                                switch (_decider(ex))
                                {
                                    case Directive.Stop:
                                        FailStage(ex);
                                        break;
                                    case Directive.Restart:
                                        RestartState();
                                        if (!HasBeenPulled(_shape.Inlet)) Pull(_shape.Inlet);
                                        break;
                                    case Directive.Resume:
                                        break;
                                }
                            }
                            _pending = Option<TIn>.None;
                        }
                    }
                    else
                    {
                        Flush();
                        if (!HasBeenPulled(_shape.Inlet))
                            Pull(_shape.Inlet);
                    }
                });
            }

            private void Flush()
            {
                if (_aggregate.HasValue)
                {
                    Push(_shape.Outlet, _aggregate.Value);
                    _left = _stage._max;
                }
                if (_pending.HasValue)
                {
                    try
                    {
                        _aggregate = _stage._seed(_pending.Value);
                        _left -= _stage._costFunc(_pending.Value);
                        _pending = Option<TIn>.None;
                    }
                    catch (Exception ex)
                    {
                        switch (_decider(ex))
                        {
                            case Directive.Stop:
                                FailStage(ex);
                                break;
                            case Directive.Restart:
                                RestartState();
                                break;
                            case Directive.Resume:
                                _pending = Option<TIn>.None;
                                break;
                        }
                    }
                }
                else
                    _aggregate = Option<TOut>.None;
            }

            public override void PreStart() => Pull(_shape.Inlet);

            private void RestartState()
            {
                _aggregate = Option<TOut>.None;
                _left = _stage._max;
                _pending = Option<TIn>.None;
            }
        }

        #endregion

        private readonly long _max;
        private readonly Func<TIn, long> _costFunc;
        private readonly Func<TIn, TOut> _seed;
        private readonly Func<TOut, TIn, TOut> _aggregate;

        public Batch(long max, Func<TIn, long> costFunc, Func<TIn, TOut> seed, Func<TOut, TIn, TOut> aggregate)
        {
            _max = max;
            _costFunc = costFunc;
            _seed = seed;
            _aggregate = aggregate;

            var inlet = new Inlet<TIn>("Batch.in");
            var outlet = new Outlet<TOut>("Batch.out");

            Shape = new FlowShape<TIn, TOut>(inlet, outlet);
        }

        public override FlowShape<TIn, TOut> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(inheritedAttributes, this);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class Expand<TIn, TOut> : GraphStage<FlowShape<TIn, TOut>>
    {
        #region internal classes

        private sealed class Logic : GraphStageLogic
        {
            private IIterator<TOut> _iterator;
            private bool _expanded;
            private readonly FlowShape<TIn, TOut> _shape;

            public Logic(Expand<TIn, TOut> stage) : base(stage.Shape)
            {
                _shape = stage.Shape;

                _iterator = new IteratorAdapter<TOut>(Enumerable.Empty<TOut>().GetEnumerator());
                SetHandler(_shape.Inlet, onPush: () =>
                {
                    _iterator = new IteratorAdapter<TOut>(stage._extrapolate(Grab(_shape.Inlet)));
                    if (_iterator.HasNext())
                    {
                        if (IsAvailable(_shape.Outlet))
                        {
                            _expanded = true;
                            Pull(_shape.Inlet);
                            Push(_shape.Outlet, _iterator.Next());
                        }
                        else
                            _expanded = false;
                    }
                    else
                        Pull(_shape.Inlet);
                }, onUpstreamFinish: () =>
                {
                    if (_iterator.HasNext() && !_expanded)
                    {
                        // need to wait
                    }
                    else
                        CompleteStage();
                });

                SetHandler(_shape.Outlet, onPull: () =>
                {
                    if (_iterator.HasNext())
                    {
                        if (!_expanded)
                        {
                            _expanded = true;
                            if (IsClosed(_shape.Inlet))
                            {
                                Push(_shape.Outlet, _iterator.Next());
                                CompleteStage();
                            }
                            else
                            {
                                // expand needs to pull first to be "fair" when upstream is not actually slow
                                Pull(_shape.Inlet);
                                Push(_shape.Outlet, _iterator.Next());
                            }
                        }
                        else
                            Push(_shape.Outlet, _iterator.Next());
                    }
                });
            }

            public override void PreStart() => Pull(_shape.Inlet);
        }

        #endregion

        private readonly Func<TIn, IEnumerator<TOut>> _extrapolate;

        public Expand(Func<TIn, IEnumerator<TOut>> extrapolate)
        {
            _extrapolate = extrapolate;

            var inlet = new Inlet<TIn>("expand.in");
            var outlet = new Outlet<TOut>("expand.out");

            Shape = new FlowShape<TIn, TOut>(inlet, outlet);
        }

        protected override Attributes InitialAttributes => DefaultAttributes.Expand;

        public override FlowShape<TIn, TOut> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        public override string ToString() => "Expand";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class SelectAsync<TIn, TOut> : GraphStage<FlowShape<TIn, TOut>>
    {
        #region internal classes

        private sealed class Logic : GraphStageLogic
        {
            private class Holder<T>
            {
                private readonly Action<Holder<T>> _callback;

                public Holder(Result<T> element, Action<Holder<T>> callback)
                {
                    _callback = callback;
                    Element = element;
                }

                public Result<T> Element { get; private set; }

                public void Invoke(Result<T> result)
                {
                    Element = result.IsSuccess && result.Value == null
                        ? Result.Failure<T>(ReactiveStreamsCompliance.ElementMustNotBeNullException)
                        : result;
                    _callback(this);
                }
            }

            private static readonly Result<TOut> NotYetThere = Result.Failure<TOut>(new Exception());

            private readonly SelectAsync<TIn, TOut> _stage;
            private readonly Decider _decider;
            private IBuffer<Holder<TOut>> _buffer;
            private readonly Action<Holder<TOut>> _taskCallback;

            public Logic(Attributes inheritedAttributes, SelectAsync<TIn, TOut> stage) : base(stage.Shape)
            {
                _stage = stage;
                var attr = inheritedAttributes.GetAttribute<ActorAttributes.SupervisionStrategy>(null);
                _decider = attr != null ? attr.Decider : Deciders.StoppingDecider;

                _taskCallback = GetAsyncCallback<Holder<TOut>>(t =>
                {
                    var element = t.Element;
                    if (!element.IsSuccess)
                    {
                        if (_decider(element.Exception) == Directive.Stop)
                        {
                            FailStage(element.Exception);
                            return;
                        }
                    }

                    if (IsAvailable(stage.Out))
                        PushOne();
                });

                SetHandler(_stage.In, onPush: () =>
                {
                    try
                    {
                        var task = _stage._mapFunc(Grab(_stage.In));
                        var holder = new Holder<TOut>(NotYetThere, _taskCallback);
                        _buffer.Enqueue(holder);
                        task.ContinueWith(continuationAction: t => holder.Invoke(Result.FromTask(t)), continuationOptions: TaskContinuationOptions.ExecuteSynchronously);
                    }
                    catch (Exception e)
                    {
                        if (_decider(e) == Directive.Stop)
                            FailStage(e);
                    }
                    if (Todo < _stage._parallelism)
                        TryPull(_stage.In);
                }, onUpstreamFinish: () =>
                {
                    if (Todo == 0)
                        CompleteStage();
                });
                SetHandler(_stage.Out, onPull: PushOne);
            }

            private int Todo => _buffer.Used;

            public override void PreStart() => _buffer = Buffer.Create<Holder<TOut>>(_stage._parallelism, Materializer);

            private void PushOne()
            {
                var inlet = _stage.In;
                while (true)
                {
                    if (_buffer.IsEmpty)
                    {
                        if (IsClosed(inlet))
                            CompleteStage();
                        else if (!HasBeenPulled(inlet))
                            Pull(inlet);
                    }
                    else if (_buffer.Peek().Element == NotYetThere)
                    {
                        if (Todo < _stage._parallelism && !HasBeenPulled(inlet))
                            TryPull(inlet);
                    }
                    else
                    {
                        var result = _buffer.Dequeue().Element;
                        if (!result.IsSuccess)
                            continue;

                        Push(_stage.Out, result.Value);
                        if (Todo < _stage._parallelism && !HasBeenPulled(inlet))
                            TryPull(inlet);
                    }

                    break;
                }
            }

            public override string ToString() => $"SelectAsync.Logic(buffer={_buffer})";
        }

        #endregion

        private readonly int _parallelism;
        private readonly Func<TIn, Task<TOut>> _mapFunc;

        public readonly Inlet<TIn> In = new Inlet<TIn>("SelectAsync.in");
        public readonly Outlet<TOut> Out = new Outlet<TOut>("SelectAsync.out");

        public SelectAsync(int parallelism, Func<TIn, Task<TOut>> mapFunc)
        {
            _parallelism = parallelism;
            _mapFunc = mapFunc;
            Shape = new FlowShape<TIn, TOut>(In, Out);
        }

        protected override Attributes InitialAttributes { get; } = Attributes.CreateName("selectAsync");

        public override FlowShape<TIn, TOut> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(inheritedAttributes, this);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class SelectAsyncUnordered<TIn, TOut> : GraphStage<FlowShape<TIn, TOut>>
    {
        #region internal classes

        private sealed class Logic : GraphStageLogic
        {
            private readonly SelectAsyncUnordered<TIn, TOut> _stage;
            private readonly Decider _decider;
            private IBuffer<TOut> _buffer;
            private readonly Action<Result<TOut>> _taskCallback;
            private int _inFlight;

            public Logic(Attributes inheritedAttributes, SelectAsyncUnordered<TIn, TOut> stage) : base(stage.Shape)
            {
                _stage = stage;
                var attr = inheritedAttributes.GetAttribute<ActorAttributes.SupervisionStrategy>(null);
                _decider = attr != null ? attr.Decider : Deciders.StoppingDecider;

                _taskCallback = GetAsyncCallback<Result<TOut>>(result =>
                {
                    _inFlight--;
                    if (result.IsSuccess && result.Value != null)
                    {
                        if (IsAvailable(stage.Out))
                        {
                            if (!HasBeenPulled(stage.In))
                                TryPull(stage.In);
                            Push(stage.Out, result.Value);
                        }
                        else
                            _buffer.Enqueue(result.Value);
                    }
                    else
                    {
                        var ex = !result.IsSuccess
                            ? result.Exception
                            : ReactiveStreamsCompliance.ElementMustNotBeNullException;
                        if (_decider(ex) == Directive.Stop)
                            FailStage(ex);
                        else if (IsClosed(stage.In) && Todo == 0)
                            CompleteStage();
                        else if (!HasBeenPulled(stage.In))
                            TryPull(stage.In);
                    }
                });

                SetHandler(_stage.In, onPush: () =>
                {
                    try
                    {
                        var task = _stage._mapFunc(Grab(_stage.In));
                        _inFlight++;
                        task.ContinueWith(t => _taskCallback(Result.FromTask(t)), TaskContinuationOptions.ExecuteSynchronously);
                    }
                    catch (Exception e)
                    {
                        if (_decider(e) == Directive.Stop)
                            FailStage(e);
                    }

                    if (Todo < _stage._parallelism)
                        TryPull(_stage.In);
                }, onUpstreamFinish: () =>
                {
                    if (Todo == 0)
                        CompleteStage();
                });
                SetHandler(_stage.Out, onPull: () =>
                {
                    var inlet = _stage.In;
                    if (!_buffer.IsEmpty)
                        Push(_stage.Out, _buffer.Dequeue());
                    else if (IsClosed(inlet) && Todo == 0)
                        CompleteStage();

                    if (Todo < _stage._parallelism && !HasBeenPulled(inlet))
                        TryPull(inlet);
                });
            }

            private int Todo => _inFlight + _buffer.Used;

            public override void PreStart() => _buffer = Buffer.Create<TOut>(_stage._parallelism, Materializer);

            public override string ToString() => $"SelectAsyncUnordered.Logic(InFlight={_inFlight}, buffer= {_buffer}";
        }

        #endregion

        private readonly int _parallelism;
        private readonly Func<TIn, Task<TOut>> _mapFunc;
        public readonly Inlet<TIn> In = new Inlet<TIn>("SelectAsyncUnordered.in");
        public readonly Outlet<TOut> Out = new Outlet<TOut>("SelectAsyncUnordered.out");

        public SelectAsyncUnordered(int parallelism, Func<TIn, Task<TOut>> mapFunc)
        {
            _parallelism = parallelism;
            _mapFunc = mapFunc;
            Shape = new FlowShape<TIn, TOut>(In, Out);
        }

        protected override Attributes InitialAttributes { get; } = Attributes.CreateName("selectAsyncUnordered");

        public override FlowShape<TIn, TOut> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(inheritedAttributes, this);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class Log<T> : PushStage<T, T>
    {
        private static readonly Attributes.LogLevels DefaultLogLevels = new Attributes.LogLevels(onElement: LogLevel.DebugLevel, onFinish: LogLevel.DebugLevel, onFailure: LogLevel.ErrorLevel);

        private readonly string _name;
        private readonly Func<T, object> _extract;
        private readonly ILoggingAdapter _adapter;
        private readonly Decider _decider;
        private Attributes.LogLevels _logLevels;
        private ILoggingAdapter _log;

        public Log(string name, Func<T, object> extract, ILoggingAdapter adapter, Decider decider)
        {
            _name = name;
            _extract = extract;
            _adapter = adapter;
            _decider = decider;
        }

        public override void PreStart(ILifecycleContext context)
        {
            _logLevels = context.Attributes.GetAttribute(DefaultLogLevels);
            _log = _adapter;
            if (_log == null)
            {
                ActorMaterializer materializer;
                try
                {
                    materializer = ActorMaterializer.Downcast(context.Materializer);
                }
                catch (Exception ex)
                {
                    throw new Exception("Log stage can only provide LoggingAdapter when used with ActorMaterializer! Provide a LoggingAdapter explicitly or use the actor based flow materializer.", ex);
                }

                _log = new BusLogging(materializer.System.EventStream, _name, GetType(), new DefaultLogMessageFormatter());
            }
        }

        public override ISyncDirective OnPush(T element, IContext<T> context)
        {
            if (IsEnabled(_logLevels.OnElement))
                _log.Log(_logLevels.OnElement, "[{0}] Element: {1}", _name, _extract(element));

            return context.Push(element);
        }

        public override ITerminationDirective OnUpstreamFailure(Exception cause, IContext<T> context)
        {
            if (IsEnabled(_logLevels.OnFailure))
                if (_logLevels.OnFailure == LogLevel.ErrorLevel)
                    _log.Error(cause, "[{0}] Upstream failed.", _name);
                else
                    _log.Log(_logLevels.OnFailure, "[{0}] Upstream failed, cause: {1} {2}", _name, cause.GetType(), cause.Message);

            return base.OnUpstreamFailure(cause, context);
        }

        public override ITerminationDirective OnUpstreamFinish(IContext<T> context)
        {
            if (IsEnabled(_logLevels.OnFinish))
                _log.Log(_logLevels.OnFinish, "[{0}] Upstream finished.", _name);

            return base.OnUpstreamFinish(context);
        }

        public override ITerminationDirective OnDownstreamFinish(IContext<T> context)
        {
            if (IsEnabled(_logLevels.OnFinish))
                _log.Log(_logLevels.OnFinish, "[{0}] Downstream finished.", _name);

            return base.OnDownstreamFinish(context);
        }

        public override Directive Decide(Exception cause) => _decider(cause);

        private bool IsEnabled(LogLevel level) => level != Attributes.LogLevels.Off;
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal enum TimerKeys
    {
        TakeWithin,
        DropWithin,
        GroupedWithin
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class GroupedWithin<T> : GraphStage<FlowShape<T, IEnumerable<T>>>
    {
        #region internal classes

        private sealed class Logic : TimerGraphStageLogic
        {
            private const string GroupedWithinTimer = "GroupedWithinTimer";

            private readonly GroupedWithin<T> _stage;
            private List<T> _buffer;

            // True if:
            // - buf is nonEmpty
            //       AND
            // - timer fired OR group is full
            private bool _groupClosed;
            private bool _groupEmitted;
            private bool _finished;
            private int _elements;

            public Logic(GroupedWithin<T> stage) : base(stage.Shape)
            {
                _stage = stage;
                _buffer = new List<T>(_stage._count);

                SetHandler(_stage._in, onPush: () =>
                {
                    if (!_groupClosed)
                        NextElement(Grab(_stage._in)); // otherwise keep the element for next round
                }, onUpstreamFinish: () =>
                {
                    _finished = true;
                    if (_groupEmitted)
                        CompleteStage();
                    else
                        CloseGroup();
                });

                SetHandler(_stage._out, onPull: () =>
                {
                    if (_groupClosed)
                        EmitGroup();
                });
            }

            public override void PreStart()
            {
                ScheduleRepeatedly(GroupedWithinTimer, _stage._timeout);
                Pull(_stage._in);
            }

            private void NextElement(T element)
            {
                _groupEmitted = false;
                _buffer.Add(element);
                _elements++;
                if (_elements == _stage._count)
                {
                    ScheduleRepeatedly(GroupedWithinTimer, _stage._timeout);
                    CloseGroup();
                }
                else
                    Pull(_stage._in);
            }

            private void CloseGroup()
            {
                _groupClosed = true;
                if (IsAvailable(_stage._out))
                    EmitGroup();
            }

            private void EmitGroup()
            {
                _groupEmitted = true;
                Push(_stage._out, _buffer);
                _buffer = new List<T>();
                if (!_finished)
                    StartNewGroup();
                else
                    CompleteStage();
            }

            private void StartNewGroup()
            {
                _elements = 0;
                _groupClosed = false;
                if (IsAvailable(_stage._in))
                    NextElement(Grab(_stage._in));
                else if (!HasBeenPulled(_stage._in))
                    Pull(_stage._in);
            }

            protected internal override void OnTimer(object timerKey)
            {
                if (_elements > 0)
                    CloseGroup();
            }
        }

        #endregion

        private readonly Inlet<T> _in = new Inlet<T>("in");
        private readonly Outlet<IEnumerable<T>> _out = new Outlet<IEnumerable<T>>("out");
        private readonly int _count;
        private readonly TimeSpan _timeout;

        public GroupedWithin(int count, TimeSpan timeout)
        {
            _count = count;
            _timeout = timeout;
            Shape = new FlowShape<T, IEnumerable<T>>(_in, _out);
        }

        protected override Attributes InitialAttributes { get; } = Attributes.CreateName("GroupedWithin");

        public override FlowShape<T, IEnumerable<T>> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class Delay<T> : SimpleLinearGraphStage<T>
    {
        #region internal classes

        private sealed class Logic : TimerGraphStageLogic
        {
            private const string TimerName = "DelayedTimer";
            private readonly Delay<T> _stage;
            private IBuffer<Tuple<long, T>> _buffer; // buffer has pairs timestamp with upstream element
            private bool _willStop;
            private readonly int _size;

            public Logic(Attributes inheritedAttributes, Delay<T> stage) : base(stage.Shape)
            {
                _stage = stage;

                var inputBuffer = inheritedAttributes.GetAttribute<Attributes.InputBuffer>(null);
                if (inputBuffer == null)
                    throw new IllegalStateException($"Couldn't find InputBuffer Attribute for {this}");
                _size = inputBuffer.Max;

                var overflowStrategy = OnPushStrategy(_stage._strategy);

                SetHandler(_stage.Inlet, onPush: () =>
                {
                    if (_buffer.IsFull) overflowStrategy();
                    else
                    {
                        GrabAndPull(_stage._strategy != DelayOverflowStrategy.Backpressure || _buffer.Capacity < _size - 1);
                        if (!IsTimerActive(TimerName))
                            ScheduleOnce(TimerName, _stage._delay);
                    }
                }, onUpstreamFinish: () =>
                {
                    if (IsAvailable(_stage.Outlet) && IsTimerActive(TimerName))
                        _willStop = true;
                    else
                        CompleteStage();
                });

                SetHandler(_stage.Outlet, onPull: () =>
                {
                    if (!IsTimerActive(TimerName) && !_buffer.IsEmpty && NextElementWaitTime < 0)
                        Push(_stage.Outlet, _buffer.Dequeue().Item2);

                    if (!_willStop && !HasBeenPulled(_stage.Inlet))
                        Pull(_stage.Inlet);
                    CompleteIfReady();
                });
            }

            private long NextElementWaitTime => (long) _stage._delay.TotalMilliseconds - (DateTime.UtcNow.Ticks - _buffer.Peek().Item1)*1000*10;

            public override void PreStart() => _buffer = Buffer.Create<Tuple<long, T>>(_size, Materializer);

            private void CompleteIfReady()
            {
                if (_willStop && _buffer.IsEmpty)
                    CompleteStage();
            }

            protected internal override void OnTimer(object timerKey)
            {
                Push(_stage.Outlet, _buffer.Dequeue().Item2);
                if (!_buffer.IsEmpty)
                {
                    var waitTime = NextElementWaitTime;
                    if (waitTime > 10)
                        ScheduleOnce(TimerName, new TimeSpan(waitTime));
                }

                CompleteIfReady();
            }

            private void GrabAndPull(bool pullCondition = true)
            {
                _buffer.Enqueue(new Tuple<long, T>(DateTime.UtcNow.Ticks, Grab(_stage.Inlet)));
                if (pullCondition)
                    Pull(_stage.Inlet);
            }

            private Action OnPushStrategy(DelayOverflowStrategy strategy)
            {
                switch (strategy)
                {
                    case DelayOverflowStrategy.EmitEarly:
                        return () =>
                        {
                            if (!IsTimerActive(TimerName))
                                Push(_stage.Outlet, _buffer.Dequeue().Item2);
                            else
                            {
                                CancelTimer(TimerName);
                                OnTimer(TimerName);
                            }
                        };
                    case DelayOverflowStrategy.DropHead:
                        return () =>
                        {
                            _buffer.DropHead();
                            GrabAndPull();
                        };
                    case DelayOverflowStrategy.DropTail:
                        return () =>
                        {
                            _buffer.DropTail();
                            GrabAndPull();
                        };
                    case DelayOverflowStrategy.DropNew:
                        return () =>
                        {
                            Grab(_stage.Inlet);
                            if (!IsTimerActive(TimerName))
                                ScheduleOnce(TimerName, _stage._delay);
                        };
                    case DelayOverflowStrategy.DropBuffer:
                        return () =>
                        {
                            _buffer.Clear();
                            GrabAndPull();
                        };
                    case DelayOverflowStrategy.Fail:
                        return () => { FailStage(new BufferOverflowException($"Buffer overflow for Delay combinator (max capacity was: {_size})!")); };
                    default:
                        return () => { throw new IllegalStateException($"Delay buffer must never overflow in {strategy} mode"); };
                }
            }
        }

        #endregion

        private readonly TimeSpan _delay;
        private readonly DelayOverflowStrategy _strategy;

        public Delay(TimeSpan delay, DelayOverflowStrategy strategy)
        {
            _delay = delay;
            _strategy = strategy;
        }

        protected override Attributes InitialAttributes { get; } = DefaultAttributes.Delay;

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(inheritedAttributes, this);

        public override string ToString() => "Delay";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class TakeWithin<T> : SimpleLinearGraphStage<T>
    {
        #region internal class

        private sealed class Logic : TimerGraphStageLogic
        {
            private readonly TakeWithin<T> _stage;

            public Logic(TakeWithin<T> stage) : base(stage.Shape)
            {
                _stage = stage;
                SetHandler(stage.Inlet, onPush: () => Push(stage.Outlet, Grab(stage.Inlet)));
                SetHandler(stage.Outlet, onPull: () => Pull(stage.Inlet));
            }

            protected internal override void OnTimer(object timerKey) => CompleteStage();

            public override void PreStart() => ScheduleOnce("TakeWithinTimer", _stage._timeout);
        }

        #endregion

        private readonly TimeSpan _timeout;

        public TakeWithin(TimeSpan timeout)
        {
            _timeout = timeout;
        }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class SkipWithin<T> : SimpleLinearGraphStage<T>
    {
        private readonly TimeSpan _timeout;

        #region internal classes

        private sealed class Logic : TimerGraphStageLogic
        {
            private readonly SkipWithin<T> _stage;
            private bool _allow;

            public Logic(SkipWithin<T> stage) : base(stage.Shape)
            {
                _stage = stage;
                SetHandler(_stage.Inlet, onPush: () =>
                {
                    if (_allow)
                        Push(_stage.Outlet, Grab(_stage.Inlet));
                    else
                        Pull(_stage.Inlet);
                });
                SetHandler(_stage.Outlet, onPull: () => Pull(_stage.Inlet));
            }

            public override void PreStart() => ScheduleOnce("DropWithinTimer", _stage._timeout);

            protected internal override void OnTimer(object timerKey) => _allow = true;
        }

        #endregion

        public SkipWithin(TimeSpan timeout)
        {
            _timeout = timeout;
        }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class Sum<T> : SimpleLinearGraphStage<T>
    {
        #region internal classes

        private sealed class Logic : GraphStageLogic
        {
            private T _aggregator;

            public Logic(Sum<T> stage) : base(stage.Shape)
            {
                var rest = new LambdaInHandler(
                    onPush: () =>
                    {
                        _aggregator = stage._reduce(_aggregator, Grab(stage.Inlet));
                        Pull(stage.Inlet);
                    },
                    onUpstreamFinish: () =>
                    {
                        Push(stage.Outlet, _aggregator);
                        CompleteStage();
                    });

                // Initial input handler
                SetHandler(stage.Inlet,
                    onPush: () =>
                    {
                        _aggregator = Grab(stage.Inlet);
                        Pull(stage.Inlet);
                        SetHandler(stage.Inlet, rest);
                    },
                    onUpstreamFinish: () => FailStage(new NoSuchElementException("sum over empty stream")));

                SetHandler(stage.Outlet, onPull: () => Pull(stage.Inlet));
            }

            public override string ToString() => $"Sum.Logic(aggregator={_aggregator}";
        }

        #endregion

        private readonly Func<T, T, T> _reduce;

        public Sum(Func<T, T, T> reduce)
        {
            _reduce = reduce;
        }

        protected override Attributes InitialAttributes { get; } = DefaultAttributes.Sum;

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        public override string ToString() => "Sum";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class RecoverWith<TOut, TMat> : SimpleLinearGraphStage<TOut>
    {
        #region internal classes

        private sealed class Logic : GraphStageLogic
        {
            private readonly RecoverWith<TOut, TMat> _recover;

            public Logic(RecoverWith<TOut, TMat> recover) : base(recover.Shape)
            {
                _recover = recover;
                SetHandler(recover.Outlet, onPull: () => Pull(recover.Inlet));
                SetHandler(recover.Inlet, onPush: () => Push(recover.Outlet, Grab(recover.Inlet)), onUpstreamFailure: OnFailure);
            }

            private void OnFailure(Exception ex)
            {
                var result = _recover._partialFunction(ex);
                if (result != null)
                    SwitchTo(result);
                else
                    FailStage(ex);
            }

            private void SwitchTo(IGraph<SourceShape<TOut>, TMat> source)
            {
                var sinkIn = new SubSinkInlet<TOut>(this, "RecoverWithSink");
                sinkIn.SetHandler(new LambdaInHandler(onPush: () =>
                {
                    if (IsAvailable(_recover.Outlet))
                    {
                        Push(_recover.Outlet, sinkIn.Grab());
                        sinkIn.Pull();
                    }
                }, onUpstreamFinish: () =>
                {
                    if (!sinkIn.IsAvailable)
                        CompleteStage();
                }, onUpstreamFailure: OnFailure));

                Action pushOut = () =>
                {
                    Push(_recover.Outlet, sinkIn.Grab());
                    if (!sinkIn.IsClosed)
                        sinkIn.Pull();
                    else
                        CompleteStage();
                };

                var outHandler = new LambdaOutHandler(onPull: () =>
                {
                    if (sinkIn.IsAvailable)
                        pushOut();
                }, onDownstreamFinish: () => sinkIn.Cancel());

                Source.FromGraph(source).RunWith(sinkIn.Sink, Interpreter.SubFusingMaterializer);
                SetHandler(_recover.Outlet, outHandler);
                sinkIn.Pull();
            }
        }

        #endregion

        private readonly Func<Exception, IGraph<SourceShape<TOut>, TMat>> _partialFunction;

        public RecoverWith(Func<Exception, IGraph<SourceShape<TOut>, TMat>> partialFunction)
        {
            _partialFunction = partialFunction;
        }

        protected override Attributes InitialAttributes { get; } = DefaultAttributes.RecoverWith;

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        public override string ToString() => "RecoverWith";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class StatefulSelectMany<TIn, TOut> : GraphStage<FlowShape<TIn, TOut>>
    {
        #region internal classes

        private sealed class Logic : GraphStageLogic
        {
            private readonly StatefulSelectMany<TIn, TOut> _stage;
            private IteratorAdapter<TOut> _currentIterator;
            private readonly Decider _decider;
            private Func<TIn, IEnumerable<TOut>> _plainConcat;

            public Logic(StatefulSelectMany<TIn, TOut> stage, Attributes inheritedAttributes) : base(stage.Shape)
            {
                _stage = stage;
                _decider = inheritedAttributes.GetAttribute(new ActorAttributes.SupervisionStrategy(Deciders.StoppingDecider)).Decider;
                _plainConcat = stage._concatFactory();

                SetHandler(stage._in, onPush: () =>
                {
                    try
                    {
                        _currentIterator = new IteratorAdapter<TOut>(_plainConcat(Grab(stage._in)).GetEnumerator());
                        PushPull();
                    }
                    catch (Exception ex)
                    {
                        var directive = _decider(ex);
                        switch (directive)
                        {
                            case Directive.Stop:
                                FailStage(ex);
                                break;
                            case Directive.Resume:
                                if (!HasBeenPulled(_stage._in))
                                    Pull(_stage._in);
                                break;
                            case Directive.Restart:
                                RestartState();
                                if (!HasBeenPulled(_stage._in))
                                    Pull(_stage._in);
                                break;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }
                    }
                }, onUpstreamFinish: () =>
                {
                    if (!HasNext)
                        CompleteStage();
                });

                SetHandler(stage._out, onPull: PushPull);
            }

            private void RestartState()
            {
                _plainConcat = _stage._concatFactory();
                _currentIterator = null;
            }

            private bool HasNext => _currentIterator != null && _currentIterator.HasNext();

            private void PushPull()
            {
                if (HasNext)
                {
                    Push(_stage._out, _currentIterator.Next());
                    if (!HasNext && IsClosed(_stage._in))
                        CompleteStage();
                }
                else if (!IsClosed(_stage._in))
                    Pull(_stage._in);
                else
                    CompleteStage();
            }
        }

        #endregion

        private readonly Func<Func<TIn, IEnumerable<TOut>>> _concatFactory;

        private readonly Inlet<TIn> _in = new Inlet<TIn>("StatefulSelectMany.in");
        private readonly Outlet<TOut> _out = new Outlet<TOut>("StatefulSelectMany.out");

        public StatefulSelectMany(Func<Func<TIn, IEnumerable<TOut>>> concatFactory)
        {
            _concatFactory = concatFactory;

            Shape = new FlowShape<TIn, TOut>(_in, _out);
        }

        protected override Attributes InitialAttributes { get; } = DefaultAttributes.StatefulSelectMany;

        public override FlowShape<TIn, TOut> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this, inheritedAttributes);

        public override string ToString() => "StatefulSelectMany";
    }
}