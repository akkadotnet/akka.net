using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Streams;
using System.Threading.Tasks;
using Akka.Event;
using Akka.Pattern;
using Akka.Streams.Stage;
using Akka.Streams.Supervision;
using Akka.Util;

namespace Akka.Streams.Implementation.Fusing
{
    internal sealed class Map<TIn, TOut> : PushStage<TIn, TOut>
    {
        private readonly Func<TIn, TOut> _func;
        private readonly Decider _decider;

        public Map(Func<TIn, TOut> func, Decider decider)
        {
            _func = func;
            _decider = decider;
        }

        public override ISyncDirective OnPush(TIn element, IContext<TOut> context)
        {
            return context.Push(_func(element));
        }

        public override Directive Decide(Exception cause)
        {
            return _decider(cause);
        }
    }

    internal sealed class Filter<T> : PushStage<T, T>
    {
        private readonly Predicate<T> _predicate;
        private readonly Decider _decider;

        public Filter(Predicate<T> predicate, Decider decider)
        {
            _predicate = predicate;
            _decider = decider;
        }

        public override ISyncDirective OnPush(T element, IContext<T> context)
        {
            return _predicate(element) ? (ISyncDirective)context.Push(element) : context.Pull();
        }

        public override Directive Decide(Exception cause)
        {
            return _decider(cause);
        }
    }

    internal sealed class TakeWhile<T> : PushStage<T, T>
    {
        private readonly Predicate<T> _predicate;
        private readonly Decider _decider;

        public TakeWhile(Predicate<T> predicate, Decider decider)
        {
            _predicate = predicate;
            _decider = decider;
        }

        public override ISyncDirective OnPush(T element, IContext<T> context)
        {
            return _predicate(element) ? (ISyncDirective)context.Push(element) : context.Finish();
        }

        public override Directive Decide(Exception cause)
        {
            return _decider(cause);
        }
    }

    internal sealed class DropWhile<T> : PushStage<T, T>
    {
        private readonly Predicate<T> _predicate;
        private readonly Decider _decider;
        private bool _taking = false;

        public DropWhile(Predicate<T> predicate, Decider decider)
        {
            _predicate = predicate;
            _decider = decider;
        }

        public override ISyncDirective OnPush(T element, IContext<T> context)
        {
            if (_taking || !_predicate(element))
            {
                _taking = true;
                return context.Push(element);
            }
            else return context.Pull();
        }

        public override Directive Decide(Exception cause)
        {
            return _decider(cause);
        }
    }

    internal sealed class Collect<TIn, TOut> : PushStage<TIn, TOut> where TOut : class
    {
        private readonly Func<TIn, TOut> _func;
        private readonly Decider _decider;

        public Collect(Func<TIn, TOut> func, Decider decider)
        {
            _func = func;
            _decider = decider;
        }

        public override ISyncDirective OnPush(TIn element, IContext<TOut> context)
        {
            var result = _func(element);
            return result == null ? (ISyncDirective)context.Pull() : context.Push(result);
        }

        public override Directive Decide(Exception cause)
        {
            return _decider(cause);
        }
    }

    internal sealed class Recover<T> : PushPullStage<T, T> where T : class
    {
        private readonly Func<Exception, T> _recovery;
        private T _recovered = null;

        public Recover(Func<Exception, T> recovery)
        {
            _recovery = recovery;
        }

        public override ISyncDirective OnPush(T element, IContext<T> context)
        {
            return context.Push(element);
        }

        public override ISyncDirective OnPull(IContext<T> context)
        {
            return _recovered != null ? (ISyncDirective)context.PushAndFinish(_recovered) : context.Pull();
        }

        public override ITerminationDirective OnUpstreamFailure(Exception cause, IContext<T> context)
        {
            var result = _recovery(cause);
            if (result == null) return context.Fail(cause);
            else
            {
                _recovered = result;
                return context.AbsorbTermination();
            }
        }
    }

    internal sealed class MapConcat<TIn, TOut> : PushPullStage<TIn, TOut>
    {
        private readonly Func<TIn, IEnumerable<TOut>> _func;
        private readonly Decider _decider;
        private IEnumerator<TOut> _currentEnumerator;

        public MapConcat(Func<TIn, IEnumerable<TOut>> func, Decider decider)
        {
            _func = func;
            _decider = decider;
            _currentEnumerator = Enumerable.Empty<TOut>().GetEnumerator();
        }

        public override ISyncDirective OnPush(TIn element, IContext<TOut> context)
        {
            _currentEnumerator = _func(element).GetEnumerator();
            return !_currentEnumerator.MoveNext() ? (ISyncDirective)context.Pull() : context.Push(_currentEnumerator.Current);
        }

        public override ISyncDirective OnPull(IContext<TOut> context)
        {
            if (context.IsFinishing)
            {
                if (_currentEnumerator.MoveNext())
                {
                    var element = _currentEnumerator.Current;
                    return _currentEnumerator.MoveNext() ? context.Push(element) : context.PushAndFinish(element);
                }
                else return context.Finish();
            }
            else
            {
                return _currentEnumerator.MoveNext() ? (ISyncDirective)context.Push(_currentEnumerator.Current) : context.Pull();
            }
        }

        public override ITerminationDirective OnUpstreamFinish(IContext<TOut> context)
        {
            return _currentEnumerator.MoveNext() ? context.AbsorbTermination() : context.Finish();
        }

        public override Directive Decide(Exception cause)
        {
            return _decider(cause);
        }

        public override IStage<TIn, TOut> Restart()
        {
            return new MapConcat<TIn, TOut>(_func, _decider);
        }
    }

    internal sealed class Take<T> : PushPullStage<T, T>
    {
        private readonly long _count;
        private long _left;
        public Take(long count)
        {
            _count = count;
            _left = count;
        }

        public override ISyncDirective OnPush(T element, IContext<T> context)
        {
            _left--;
            if (_left > 0) return context.Push(element);
            else if (_left == 0) return context.PushAndFinish(element);
            else return context.Finish();
        }

        public override ISyncDirective OnPull(IContext<T> context)
        {
            return _left <= 0 ? context.Finish() : context.Pull();
        }
    }

    internal sealed class Drop<T> : PushStage<T, T>
    {
        private readonly long _count;
        private long _left;
        public Drop(long count)
        {
            _count = _left = count;
        }

        public override ISyncDirective OnPush(T element, IContext<T> context)
        {
            if (_left > 0)
            {
                _left--;
                return context.Pull();
            }
            else return context.Push(element);
        }
    }

    internal sealed class Scan<TIn, TOut> : PushPullStage<TIn, TOut>
    {
        private readonly TOut _zero;
        private readonly Func<TOut, TIn, TOut> _aggregate;
        private readonly Decider _decider;
        private TOut _aggregator;
        private bool _pushedZero = false;

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
            else
            {
                _aggregator = _aggregate(_zero, element);
                return context.Push(_zero);
            }
        }

        public override ISyncDirective OnPull(IContext<TOut> context)
        {
            if (!_pushedZero)
            {
                _pushedZero = true;
                return context.IsFinishing ? context.PushAndFinish(_aggregator) : context.Push(_aggregator);
            }
            else return context.Pull();
        }

        public override ITerminationDirective OnUpstreamFinish(IContext<TOut> context)
        {
            return _pushedZero ? context.Finish() : context.AbsorbTermination();
        }

        public override Directive Decide(Exception cause)
        {
            return _decider(cause);
        }

        public override IStage<TIn, TOut> Restart()
        {
            return new Scan<TIn, TOut>(_zero, _aggregate, _decider);
        }
    }

    internal sealed class Fold<TIn, TOut> : PushPullStage<TIn, TOut>
    {
        private readonly TOut _zero;
        private readonly Func<TOut, TIn, TOut> _aggregate;
        private readonly Decider _decider;
        private TOut _aggregator;

        public Fold(TOut zero, Func<TOut, TIn, TOut> aggregate, Decider decider)
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

        public override ISyncDirective OnPull(IContext<TOut> context)
        {
            return context.IsFinishing ? (ISyncDirective)context.PushAndFinish(_aggregator) : context.Pull();
        }

        public override ITerminationDirective OnUpstreamFinish(IContext<TOut> context)
        {
            return context.AbsorbTermination();
        }

        public override Directive Decide(Exception cause)
        {
            return _decider(cause);
        }

        public override IStage<TIn, TOut> Restart()
        {
            return new Fold<TIn, TOut>(_zero, _aggregate, _decider);
        }
    }

    internal sealed class Intersperse<T> : GraphStage<FlowShape<T, T>>
    {
        #region internal class
        private sealed class StartInHandler : InHandler
        {
            private readonly Intersperse<T> _stage;
            private readonly AnonymousGraphStageLogic _logic;

            public StartInHandler(Intersperse<T> stage, AnonymousGraphStageLogic logic)
            {
                _stage = stage;
                _logic = logic;
            }

            public override void OnPush()
            {
                // if else (to avoid using Iterator[T].flatten in hot code)
                if (_stage.InjectStartEnd) _logic.EmitMultiple(_stage.Out, new[] { _stage._start, _logic.Grab(_stage.In) });
                else _logic.Emit(_stage.Out, _logic.Grab(_stage.In));
                _logic.SetHandler(_stage.In, new RestInHandler(_stage, _logic));
            }

            public override void OnUpstreamFinish()
            {
                _logic.EmitMultiple(_stage.Out, new[] { _stage._start, _stage._end });
                _logic.CompleteStage();
            }
        }
        private sealed class RestInHandler : InHandler
        {
            private readonly Intersperse<T> _stage;
            private readonly AnonymousGraphStageLogic _logic;

            public RestInHandler(Intersperse<T> stage, AnonymousGraphStageLogic logic)
            {
                _stage = stage;
                _logic = logic;
            }

            public override void OnPush()
            {
                _logic.EmitMultiple(_stage.Out, new[] { _stage._inject, _logic.Grab(_stage.In) });
            }

            public override void OnUpstreamFinish()
            {
                if (_stage.InjectStartEnd) _logic.Emit(_stage.Out, _stage._end);
                _logic.CompleteStage();
            }
        }
        private sealed class AnonymousGraphStageLogic : GraphStageLogic
        {
            public AnonymousGraphStageLogic(Shape shape, Intersperse<T> stage) : base(shape)
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

        public bool InjectStartEnd { get; private set; }

        public override FlowShape<T, T> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new AnonymousGraphStageLogic(Shape, this);
        }
    }

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
            else return context.Pull();
        }

        public override ISyncDirective OnPull(IContext<IEnumerable<T>> context)
        {
            if (context.IsFinishing)
            {
                var result = _buffer;
                _left = _count;
                _buffer = new List<T>(_count);
                return context.Push(result);
            }
            else return context.Pull();
        }

        public override ITerminationDirective OnUpstreamFinish(IContext<IEnumerable<T>> context)
        {
            return _left == _count ? context.Finish() : context.AbsorbTermination();
        }
    }

    internal sealed class LimitWeighted<T> : PushStage<T, T>
    {
        private readonly long _max;
        private readonly Func<T, long> _costFunc;
        private long _left;

        public LimitWeighted(long max, Func<T, long> costFunc)
        {
            _left = _max = max;
            _costFunc = costFunc;
        }

        public override ISyncDirective OnPush(T element, IContext<T> context)
        {
            _left -= _costFunc(element);
            if (_left >= 0) return context.Push(element);
            else return context.Fail(new StreamLimitReachedException(_max));
        }
    }

    internal sealed class Sliding<T> : PushPullStage<T, IEnumerable<T>>
    {
        private readonly int _count;
        private readonly int _step;
        private List<T> _buffer;

        public Sliding(int count, int step)
        {
            _count = count;
            _step = step;
            _buffer = new List<T>();
        }

        public override ISyncDirective OnPush(T element, IContext<IEnumerable<T>> context)
        {
            _buffer.Add(element);
            if (_buffer.Count < _count) return context.Pull();
            if (_buffer.Count == _count) return context.Push(_buffer);
            if (_step > _count)
            {
                if (_buffer.Count == _step) _buffer = new List<T>();
                return context.Pull();
            }
            else
            {
                _buffer = _buffer.Skip(_step).ToList();
                return _buffer.Count == _count ? (ISyncDirective)context.Push(_buffer) : context.Pull();
            }
        }

        public override ISyncDirective OnPull(IContext<IEnumerable<T>> context)
        {
            if (!context.IsFinishing) return context.Pull();
            if (_buffer.Count >= _count) return context.Finish();
            return context.PushAndFinish(_buffer);
        }

        public override ITerminationDirective OnUpstreamFinish(IContext<IEnumerable<T>> context)
        {
            return _buffer.Count == 0 ? context.Finish() : context.AbsorbTermination();
        }
    }

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

        public override void PreStart(ILifecycleContext context)
        {
            _buffer = Buffer.Create<T>(_count, Context.Materializer);
        }

        public override IUpstreamDirective OnPush(T element, IDetachedContext<T> context)
        {
            return context.IsHoldingDownstream ? context.PushAndPull(element) : _enqueueAction(context, element);
        }

        public override IDownstreamDirective OnPull(IDetachedContext<T> context)
        {
            if (context.IsFinishing)
            {
                var element = _buffer.Dequeue();
                return _buffer.IsEmpty ? context.PushAndFinish(element) : context.Push(element);
            }
            if (_buffer.IsEmpty) return context.HoldDownstream();
            return context.Push(_buffer.Dequeue());
        }

        public override ITerminationDirective OnUpstreamFinish(IDetachedContext<T> context)
        {
            return _buffer.IsEmpty ? context.Finish() : context.AbsorbTermination();
        }

        private Func<IDetachedContext<T>, T, IUpstreamDirective> EnqueueAction(OverflowStrategy overflowStrategy)
        {
            switch (overflowStrategy)
            {
                case OverflowStrategy.DropHead:
                    return (context, element) =>
                    {
                        if (_buffer.IsFull) _buffer.DropHead();
                        _buffer.Enqueue(element);
                        return context.Pull();
                    };
                case OverflowStrategy.DropTail:
                    return (context, element) =>
                    {
                        if (_buffer.IsFull) _buffer.DropTail();
                        _buffer.Enqueue(element);
                        return context.Pull();
                    };
                case OverflowStrategy.DropBuffer:
                    return (context, element) =>
                    {
                        if (_buffer.IsFull) _buffer.Clear();
                        _buffer.Enqueue(element);
                        return context.Pull();
                    };
                case OverflowStrategy.DropNew:
                    return (context, element) =>
                    {
                        if (!_buffer.IsFull) _buffer.Enqueue(element);
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
                            return context.Fail(new BufferOverflowException(string.Format("Buffer overflow (max capacity was {0})", _count)));
                        else
                        {
                            _buffer.Enqueue(element);
                            return context.Pull();
                        }
                    };
                default: throw new NotSupportedException(string.Format("Overflow strategy {0} is not supported", overflowStrategy));
            }
        }
    }

    internal sealed class Completed<T> : PushPullStage<T, T>
    {
        public override ISyncDirective OnPush(T element, IContext<T> context)
        {
            return context.Finish();
        }

        public override ISyncDirective OnPull(IContext<T> context)
        {
            return context.Finish();
        }
    }

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

    internal sealed class Conflate<TIn, TOut> : DetachedStage<TIn, TOut>
    {
        private readonly Func<TIn, TOut> _seed;
        private readonly Func<TOut, TIn, TOut> _aggregate;
        private readonly Decider _decider;
        private TOut _aggregated;
        private bool _initialized = false;

        public Conflate(Func<TIn, TOut> seed, Func<TOut, TIn, TOut> aggregate, Decider decider)
        {
            _seed = seed;
            _aggregate = aggregate;
            _decider = decider;
        }

        public override IUpstreamDirective OnPush(TIn element, IDetachedContext<TOut> context)
        {
            if (!_initialized)
            {
                _aggregated = _seed(element);
                _initialized = true;
            }
            else _aggregated = _aggregate(_aggregated, element);

            if (!context.IsHoldingDownstream) return context.Pull();
            else
            {
                var result = _aggregate;
                _initialized = false;
                return context.PushAndPull(result);
            }
        }

        public override IDownstreamDirective OnPull(IDetachedContext<TOut> context)
        {
            if (context.IsFinishing)
            {
                if (!_initialized) return context.Finish();
                else
                {
                    var result = _aggregated;
                    _initialized = false;
                    return context.PushAndFinish(result);
                }
            }
            else if (!_initialized) return context.HoldDownstream();
            else
            {
                var result = _aggregated;
                _initialized = false;
                return context.Push(result);
            }
        }

        public override ITerminationDirective OnUpstreamFinish(IDetachedContext<TOut> context)
        {
            return context.AbsorbTermination();
        }

        public override Directive Decide(Exception cause)
        {
            return _decider(cause);
        }

        public override IStage<TIn, TOut> Restart()
        {
            return new Conflate<TIn, TOut>(_seed, _aggregate, _decider);
        }
    }

    internal sealed class Expand<TIn, TOut, TSeed> : DetachedStage<TIn, TOut>
    {
        private readonly Func<TIn, TSeed> _seedFunc;
        private readonly Func<TSeed, Tuple<TOut, TSeed>> _extrapolate;

        private TSeed _seed;
        private bool _hasStarted = false;
        private bool _hasExpanded = false;

        public Expand(Func<TIn, TSeed> seedFunc, Func<TSeed, Tuple<TOut, TSeed>> extrapolate)
        {
            _seedFunc = seedFunc;
            _extrapolate = extrapolate;
        }

        public override IUpstreamDirective OnPush(TIn element, IDetachedContext<TOut> context)
        {
            _seed = _seedFunc(element);
            _hasStarted = true;
            _hasExpanded = false;
            if (context.IsHoldingDownstream)
            {
                var t = _extrapolate(_seed);
                _seed = t.Item2;
                _hasExpanded = true;
                return context.PushAndPull(t.Item1);
            }
            else return context.HoldUpstream();
        }

        public override IDownstreamDirective OnPull(IDetachedContext<TOut> context)
        {
            if (context.IsFinishing)
            {
                return !_hasStarted ? context.Finish() : context.PushAndFinish(_extrapolate(_seed).Item1);
            }
            else if (!_hasStarted) return context.HoldDownstream();
            else
            {
                var t = _extrapolate(_seed);
                _seed = t.Item2;
                _hasExpanded = true;
                return context.IsHoldingUpstream ? context.PushAndPull(t.Item1) : context.Push(t.Item1);
            }
        }

        public override ITerminationDirective OnUpstreamFinish(IDetachedContext<TOut> context)
        {
            return _hasExpanded ? context.Finish() : context.AbsorbTermination();
        }

        public override Directive Decide(Exception cause)
        {
            return Directive.Stop;
        }

        public override IStage<TIn, TOut> Restart()
        {
            throw new NotSupportedException("Expand doesn't support restart");
        }
    }

    internal sealed class MapAsync<TIn, TOut> : GraphStage<FlowShape<TIn, TOut>>
    {
        #region internal classes
        private sealed class MapAsyncGraphStageLogic : GraphStageLogic
        {
            private class Holder<T> {public Result<T> Elem { get; set; }}
            private static readonly Result<TOut> NotYetThere = Result.Failure<TOut>(new Exception());

            private readonly MapAsync<TIn, TOut> _stage;
            private readonly Decider _decider;
            private IBuffer<Holder<TOut>> _buffer;
            private readonly Action<Tuple<Holder<TOut>, Result<TOut>>> _taskCallback;

            public MapAsyncGraphStageLogic(Shape shape, Attributes inheritedAttributes, MapAsync<TIn, TOut> stage) : base(shape)
            {
                _stage = stage;
                var attr = inheritedAttributes.GetAttribute<ActorAttributes.SupervisionStrategy>(null);
                _decider = attr != null ? attr.Decider : Deciders.StoppingDecider;
               
                _taskCallback = GetAsyncCallback<Tuple<Holder<TOut>, Result<TOut>>>(t =>
                {
                    var holder = t.Item1;
                    var result = t.Item2;
                    if (!result.IsSuccess)
                        FailOrPull(holder, result);
                    else
                    {
                        if (result.Value == null)
                            FailOrPull(holder, Result.Failure<TOut>(ReactiveStreamsCompliance.ElementMustNotBeNullException));
                        else
                        {
                            holder.Elem = result;
                            if (IsAvailable(_stage.Out)) PushOne();
                        }
                    }
                });

                SetHandler(_stage.In, onPush: () =>
                {
                    try
                    {
                        var task = _stage.MapFunc(Grab(_stage.In));
                        var holder = new Holder<TOut>() {Elem = NotYetThere};
                        _buffer.Enqueue(holder);
                        task.ContinueWith(t => _taskCallback(Tuple.Create(holder, Result.FromTask(t))), TaskContinuationOptions.AttachedToParent);
                    }
                    catch (Exception e)
                    {
                        if (_decider(e) == Directive.Stop) FailStage(e);
                    }
                    if (Todo < _stage.Parallelism) TryPull(_stage.In);
                }, onUpstreamFinish: () => { if (Todo == 0) CompleteStage(); });
                SetHandler(_stage.Out, onPull: PushOne);
            }

            public int Todo { get { return _buffer.Used; } }

            public override void PreStart()
            {
                _buffer = Buffer.Create<Holder<TOut>>(_stage.Parallelism, Materializer);
            }

            private void FailOrPull(Holder<TOut> holder, Result<TOut> failure)
            {
                if (_decider(failure.Exception) == Directive.Stop) FailStage(failure.Exception);
                else
                {
                    holder.Elem = failure;
                    if (IsAvailable(_stage.Out)) PushOne();
                }
            }

            private void PushOne()
            {
                var inlet = _stage.In;
                while (true)
                {
                    if (_buffer.IsEmpty)
                    {
                        if (IsClosed(inlet)) CompleteStage();
                        else if (!HasBeenPulled(inlet)) Pull(inlet);
                    }
                    else if (_buffer.Peek().Elem == NotYetThere)
                    {
                        if (Todo < _stage.Parallelism && !HasBeenPulled(inlet)) TryPull(inlet);
                    }
                    else
                    {
                        var result = _buffer.Dequeue().Elem;
                        if (!result.IsSuccess) continue;
                        else
                        {
                            Push(_stage.Out, result.Value);
                            if (Todo < _stage.Parallelism && !HasBeenPulled(inlet)) TryPull(inlet);
                        }
                    }

                    break;
                }
            }
        }
        #endregion

        public readonly int Parallelism;
        public readonly Func<TIn, Task<TOut>> MapFunc;
        public readonly Inlet<TIn> In = new Inlet<TIn>("in");
        public readonly Outlet<TOut> Out = new Outlet<TOut>("out");

        public MapAsync(int parallelism, Func<TIn, Task<TOut>> mapFunc)
        {
            Parallelism = parallelism;
            MapFunc = mapFunc;
            Shape = new FlowShape<TIn, TOut>(In, Out);
            InitialAttributes = Attributes.CreateName("MapAsync");
        }

        protected override Attributes InitialAttributes { get; }
        public override FlowShape<TIn, TOut> Shape { get; }
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new MapAsyncGraphStageLogic(Shape, inheritedAttributes, this);
        }
    }

    internal sealed class MapAsyncUnordered<TIn, TOut> : GraphStage<FlowShape<TIn, TOut>>
    {
        #region internal classes
        private sealed class MapAsyncUnorderedGraphStageLogic : GraphStageLogic
        {
            private readonly MapAsyncUnordered<TIn, TOut> _stage;
            private readonly Decider _decider;
            private IBuffer<TOut> _buffer;
            private readonly Action<Result<TOut>> _taskCallback;
            private int _inFlight = 0;

            public MapAsyncUnorderedGraphStageLogic(Shape shape, Attributes inheritedAttributes, MapAsyncUnordered<TIn, TOut> stage) : base(shape)
            {
                _stage = stage;
                var attr = inheritedAttributes.GetAttribute<ActorAttributes.SupervisionStrategy>(null);
                _decider = attr != null ? attr.Decider : Deciders.StoppingDecider;
                _taskCallback = GetAsyncCallback<Result<TOut>>(result =>
                {
                    _inFlight--;

                    if (!result.IsSuccess) FailOrPull(result.Exception);
                    else
                    {
                        if (result.Value == null) FailOrPull(ReactiveStreamsCompliance.ElementMustNotBeNullException);
                        else if (IsAvailable(_stage.Out))
                        {
                            if (!HasBeenPulled(_stage.In)) TryPull(_stage.In);
                            Push(_stage.Out, result.Value);
                        }
                        else _buffer.Enqueue(result.Value);
                    }
                });

                SetHandler(_stage.In, onPush: () =>
                {
                    try
                    {
                        var task = _stage.MapFunc(Grab(_stage.In));
                        _inFlight++;
                        task.ContinueWith(t => _taskCallback(Result.FromTask(t)), TaskContinuationOptions.AttachedToParent);
                    }
                    catch (Exception e)
                    {
                        if (_decider(e) == Directive.Stop) FailStage(e);
                    }

                    if (Todo < _stage.Parallelism) TryPull(_stage.In);
                },
                onUpstreamFinish: () => { if (Todo == 0) CompleteStage(); });
                SetHandler(_stage.Out, onPull: () =>
                {
                    var inlet = _stage.In;
                    if (!_buffer.IsEmpty) Push(_stage.Out, _buffer.Dequeue());
                    else if (IsClosed(inlet) && Todo == 0) CompleteStage();

                    if (Todo < _stage.Parallelism && !HasBeenPulled(inlet)) TryPull(inlet);
                });
            }

            public int Todo { get { return _inFlight + _buffer.Used; } }

            public override void PreStart()
            {
                _buffer = Buffer.Create<TOut>(_stage.Parallelism, Materializer);
            }

            private void FailOrPull(Exception failure)
            {
                var inlet = _stage.In;
                if (_decider(failure) == Directive.Stop) FailStage(failure);
                else if (IsClosed(inlet) && Todo == 0) CompleteStage();
                else if (!HasBeenPulled(inlet)) TryPull(inlet);
            }
        }
        #endregion

        public readonly int Parallelism;
        public readonly Func<TIn, Task<TOut>> MapFunc;
        public readonly Inlet<TIn> In = new Inlet<TIn>("in");
        public readonly Outlet<TOut> Out = new Outlet<TOut>("out");

        public MapAsyncUnordered(int parallelism, Func<TIn, Task<TOut>> mapFunc)
        {
            Parallelism = parallelism;
            MapFunc = mapFunc;
            Shape = new FlowShape<TIn, TOut>(In, Out);
            InitialAttributes = Attributes.CreateName("MapAsyncUnordered");
        }

        protected override Attributes InitialAttributes { get; }
        public override FlowShape<TIn, TOut> Shape { get; }
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new MapAsyncUnorderedGraphStageLogic(Shape, inheritedAttributes, this);
        }
    }

    internal sealed class Log<T> : PushStage<T, T>
    {
        private static readonly string DefaultLoggerName = "Akka.Streams.Log";
        private static Attributes.LogLevels DefaultLogLevels = new Attributes.LogLevels(onElement: LogLevel.DebugLevel, onFinish: LogLevel.DebugLevel, onFailure: LogLevel.ErrorLevel);

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
            _logLevels = context.Attributes.GetLogLevels() ?? DefaultLogLevels;
            base.PreStart(context);
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
                if (_logLevels.OnFailure == LogLevel.ErrorLevel) _log.Error(cause, "[{0}] Upstream failed.", _name);
                else _log.Log(_logLevels.OnFailure, "[{0}] Upstream failed, cause: {1} {2}", _name, cause.GetType(), cause.Message);

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

        public override Directive Decide(Exception cause)
        {
            return _decider(cause);
        }

        private bool IsEnabled(LogLevel level)
        {
            return (int)level != Attributes.LogLevels.Off;
        }
    }

    internal enum TimerKeys
    {
        TakeWithin,
        DropWithin,
        GroupedWithin
    }

    internal sealed class GroupedWithin<T> : GraphStage<FlowShape<T, IEnumerable<T>>>
    {
        #region internal classes
        private sealed class GroupedTimerGraphStageLogic : TimerGraphStageLogic
        {
            private const string GroupedWithinTimer = "GroupedWithinTimer";

            private readonly GroupedWithin<T> _stage;
            private List<T> _buffer;

            // True if:
            // - buf is nonEmpty
            //       AND
            // - timer fired OR group is full
            private bool _groupClosed = false;
            private bool _finished = false;
            private int _elements = 0;

            public GroupedTimerGraphStageLogic(Shape shape, GroupedWithin<T> stage) : base(shape)
            {
                _stage = stage;
                _buffer = new List<T>(_stage.Count);

                SetHandler(_stage.In, onPush: () =>
                {
                    if (!_groupClosed) NextElement(Grab(_stage.In));    // otherwise keep the element for next round
                },
                onUpstreamFinish: () =>
                {
                    _finished = true;
                    if (!_groupClosed && _elements > 0) CloseGroup();
                    else CompleteStage();
                },
                onUpstreamFailure: FailStage);
            }

            public override void PreStart()
            {
                ScheduleRepeatedly(GroupedWithinTimer, _stage.Timeout);
                Pull(_stage.In);
            }

            private void NextElement(T element)
            {
                _buffer.Add(element);
                _elements++;
                if (_elements == _stage.Count)
                {
                    ScheduleRepeatedly(GroupedWithinTimer, _stage.Timeout);
                    CloseGroup();
                }
                else Pull(_stage.In);
            }

            private void CloseGroup()
            {
                _groupClosed = true;
                if (IsAvailable(_stage.Out)) EmitGroup();
            }

            private void EmitGroup()
            {
                Push(_stage.Out, _buffer);
                _buffer = new List<T>();
                if (!_finished) StartNewGroup();
                else CompleteStage();
            }

            private void StartNewGroup()
            {
                _elements = 0;
                _groupClosed = false;
                if (IsAvailable(_stage.In)) NextElement(Grab(_stage.In));
                else if (!HasBeenPulled(_stage.In)) Pull(_stage.In);
            }

            protected internal override void OnTimer(object timerKey)
            {
                if (_elements > 0) CloseGroup();
            }
        }
        #endregion

        public readonly Inlet<T> In = new Inlet<T>("in");
        public readonly Outlet<IEnumerable<T>> Out = new Outlet<IEnumerable<T>>("out");
        public readonly int Count;
        public readonly TimeSpan Timeout;

        public GroupedWithin(int count, TimeSpan timeout)
        {
            Count = count;
            Timeout = timeout;
            Shape = new FlowShape<T, IEnumerable<T>>(In, Out);
            InitialAttributes = Attributes.CreateName("GroupedWithin");
        }

        protected override Attributes InitialAttributes { get; }
        public override FlowShape<T, IEnumerable<T>> Shape { get; }
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new GroupedTimerGraphStageLogic(Shape, this);
        }
    }

    internal sealed class Delay<T> : SimpleLinearGraphStage<T>
    {
        #region internal classes

        private sealed class DelayGraphStageLogic : TimerGraphStageLogic
        {
            private const string TimerName = "DelayedTimer";
            private readonly Delay<T> _stage;
            private IBuffer<KeyValuePair<DateTime, T>> _buffer;   // buffer has pairs timestamp with upstream element
            private bool _willStop = false;
            private readonly int _size;

            public DelayGraphStageLogic(FlowShape<T, T> shape, Attributes inheritedAttributes, Delay<T> stage) : base(shape)
            {
                _stage = stage;
                _size = inheritedAttributes.GetAttribute<Attributes.InputBuffer>(new Attributes.InputBuffer(16, 16)).Max;
                
                var overflowStrategy = OnPushStrategy(_stage._strategy);

                SetHandler(_stage.Inlet, onPush: () =>
                {
                    if (_buffer.IsFull) overflowStrategy();
                    else
                    {
                        GrabAndPull(_stage._strategy != DelayOverflowStrategy.Backpressure || _buffer.Used < _size - 1);
                        if (!IsTimerActive(TimerName)) ScheduleOnce(TimerName, _stage._delay);
                    }
                },
                onUpstreamFinish: () =>
                {
                    if (IsAvailable(_stage.Outlet) && IsTimerActive(TimerName)) _willStop = true;
                    else CompleteStage();
                });

                SetHandler(_stage.Outlet, onPull: () =>
                {
                    if (!IsTimerActive(TimerName) && !_buffer.IsEmpty && NextElementWaitTime < 0) Push(_stage.Outlet, _buffer.Dequeue().Value);
                    if (!_willStop && !HasBeenPulled(_stage.Inlet)) Pull(_stage.Inlet);
                    CompleteIfReady();
                });
            }

            private long NextElementWaitTime
            {
                get { return (long)(_stage._delay.Ticks - (DateTime.UtcNow.Ticks - _buffer.Peek().Key.Ticks)) * TimeSpan.TicksPerSecond; }
            }

            public override void PreStart()
            {
                _buffer = Buffer.Create<KeyValuePair<DateTime, T>>(_size, Materializer);
            }

            private void CompleteIfReady()
            {
                if (_willStop && _buffer.IsEmpty) CompleteStage();
            }

            protected internal override void OnTimer(object timerKey)
            {
                Push(_stage.Outlet, _buffer.Dequeue().Value);
                if (!_buffer.IsEmpty)
                {
                    var waitTime = NextElementWaitTime;
                    if (waitTime > 10) ScheduleOnce(TimerName, new TimeSpan(waitTime));
                }

                CompleteIfReady();
            }

            private void GrabAndPull(bool predicate = true)
            {
                _buffer.Enqueue(new KeyValuePair<DateTime, T>(DateTime.UtcNow, Grab(_stage.Inlet)));
                if (predicate) Pull(_stage.Inlet);
            }

            private Action OnPushStrategy(DelayOverflowStrategy strategy)
            {
                switch (strategy)
                {
                    case DelayOverflowStrategy.EmitEarly:
                        return () =>
                        {
                            if (!IsTimerActive(TimerName)) Push(_stage.Outlet, _buffer.Dequeue().Value);
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
                            if (!IsTimerActive(TimerName)) ScheduleOnce(TimerName, _stage._delay);
                        };
                    case DelayOverflowStrategy.DropBuffer:
                        return () =>
                        {
                            _buffer.Clear();
                            GrabAndPull();
                        };
                    case DelayOverflowStrategy.Fail:
                        return () =>
                        {
                            FailStage(new BufferOverflowException("Buffer overflow for Delay combinator"));
                        };
                    default:
                        return () =>
                        {
                            throw new IllegalStateException(string.Format("Delay buffer must never overflow in {0} mode", strategy));
                        };
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

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new DelayGraphStageLogic(Shape, inheritedAttributes, this);
        }
    }

    internal sealed class TakeWithin<T> : SimpleLinearGraphStage<T>
    {
        #region internal class
        private sealed class TakeWithinStageLogic : TimerGraphStageLogic
        {
            private readonly TakeWithin<T> _stage;

            public TakeWithinStageLogic(Shape shape, TakeWithin<T> stage) : base(shape)
            {
                _stage = stage;
                SetHandler(stage.Inlet, onPush: () => Push(stage.Outlet, Grab(stage.Inlet)));
                SetHandler(stage.Outlet, onPull: () => Pull(stage.Inlet));
            }

            protected internal override void OnTimer(object timerKey)
            {
                CompleteStage();
            }

            public override void PreStart()
            {
                ScheduleOnce("TakeWithinTimer", _stage._timeout);
            }
        }
        #endregion

        private readonly TimeSpan _timeout;

        public TakeWithin(TimeSpan timeout)
        {
            _timeout = timeout;
        }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new TakeWithinStageLogic(Shape, this);
        }
    }

    internal sealed class DropWithin<T> : SimpleLinearGraphStage<T>
    {
        private readonly TimeSpan _timeout;

        #region internal classes

        private sealed class DropWithinStageLogic : TimerGraphStageLogic
        {
            private readonly DropWithin<T> _stage;
            private bool _allow = false;

            public DropWithinStageLogic(Shape shape, DropWithin<T> stage) : base(shape)
            {
                _stage = stage;
                SetHandler(_stage.Inlet, onPush: () =>
                {
                    if (_allow) Push(_stage.Outlet, Grab(_stage.Inlet));
                    else Pull(_stage.Inlet);
                });
                SetHandler(_stage.Outlet, onPull: () => Pull(_stage.Inlet));
            }

            public override void PreStart()
            {
                ScheduleOnce("DropWithinTimer", _stage._timeout);
            }

            protected internal override void OnTimer(object timerKey)
            {
                _allow = true;
            }
        }
        #endregion

        public DropWithin(TimeSpan timeout)
        {
            _timeout = timeout;
        }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new DropWithinStageLogic(Shape, this);
        }
    }
}