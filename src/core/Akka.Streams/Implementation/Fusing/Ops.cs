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
using Akka.Util.Internal;

namespace Akka.Streams.Implementation.Fusing
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    public sealed class Select<TIn, TOut> : GraphStage<FlowShape<TIn, TOut>>
    {
        #region Logic

        private sealed class Logic : InAndOutGraphStageLogic
        {
            private readonly Select<TIn, TOut> _stage;
            private readonly Decider _decider;

            public Logic(Select<TIn, TOut> stage, Attributes inheritedAttributes) : base(stage.Shape)
            {
                _stage = stage;
                var attr = inheritedAttributes.GetAttribute<ActorAttributes.SupervisionStrategy>(null);
                _decider = attr != null ? attr.Decider : Deciders.StoppingDecider;

                SetHandler(stage.In, this);
                SetHandler(stage.Out, this);
            }

            public override void OnPush()
            {
                try
                {
                    Push(_stage.Out, _stage._func(Grab(_stage.In)));
                }
                catch (Exception ex)
                {
                    if (_decider(ex) == Directive.Stop)
                        FailStage(ex);
                    else
                        Pull(_stage.In);
                }
            }

            public override void OnPull() => Pull(_stage.In);
        }

        #endregion

        private readonly Func<TIn, TOut> _func;

        public Select(Func<TIn, TOut> func)
        {
            _func = func;

            Shape = new FlowShape<TIn, TOut>(In, Out);
        }

        protected override Attributes InitialAttributes { get; } = DefaultAttributes.Select;

        public Inlet<TIn> In { get; } = new Inlet<TIn>("Select.in");

        public Outlet<TOut> Out { get; } = new Outlet<TOut>("Select.out");

        public override FlowShape<TIn, TOut> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            => new Logic(this, inheritedAttributes);

        public override string ToString() => "Select";

    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    public sealed class Where<T> : SimpleLinearGraphStage<T>
    {
        #region Logic

        private sealed class Logic : InAndOutGraphStageLogic
        {
            private readonly Where<T> _stage;
            private readonly Decider _decider;

            public Logic(Where<T> stage, Attributes inheritedAttributes) : base(stage.Shape)
            {
                _stage = stage;
                var attr = inheritedAttributes.GetAttribute<ActorAttributes.SupervisionStrategy>(null);
                _decider = attr != null ? attr.Decider : Deciders.StoppingDecider;

                SetHandler(stage.Inlet, this);
                SetHandler(stage.Outlet, this);
            }
            public override void OnPush()
            {
                try
                {
                    var element = Grab(_stage.Inlet);
                    if (_stage._predicate(element))
                        Push(_stage.Outlet, element);
                    else
                        Pull(_stage.Inlet);
                }
                catch (Exception ex)
                {
                    if (_decider(ex) == Directive.Stop)
                        FailStage(ex);
                    else
                        Pull(_stage.Inlet);
                }
            }

            public override void OnPull() => Pull(_stage.Inlet);

            public override string ToString() => "WhereLogic";
        }

        #endregion

        private readonly Predicate<T> _predicate;

        public Where(Predicate<T> predicate)
        {
            _predicate = predicate;
        }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            => new Logic(this, inheritedAttributes);

        public override string ToString() => "Where";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    public sealed class TakeWhile<T> : SimpleLinearGraphStage<T>
    {
        #region Logic

        private sealed class Logic : InAndOutGraphStageLogic
        {
            private readonly TakeWhile<T> _stage;
            private readonly Decider _decider;

            public Logic(TakeWhile<T> stage, Attributes inheritedAttributes) : base(stage.Shape)
            {
                _stage = stage;
                var attr = inheritedAttributes.GetAttribute<ActorAttributes.SupervisionStrategy>(null);
                _decider = attr != null ? attr.Decider : Deciders.StoppingDecider;

                SetHandler(stage.Outlet, this);
                SetHandler(stage.Inlet, this);
            }

            public override void OnPush()
            {
                try
                {
                    var element = Grab(_stage.Inlet);
                    if (_stage._predicate(element))
                        Push(_stage.Outlet, element);
                    else
                        CompleteStage();
                }
                catch (Exception ex)
                {
                    if (_decider(ex) == Directive.Stop)
                        FailStage(ex);
                    else
                        Pull(_stage.Inlet);
                }
            }

            public override void OnPull() => Pull(_stage.Inlet);

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
    public sealed class SkipWhile<T> : GraphStage<FlowShape<T, T>>
    {
        #region Logic

        private sealed class Logic : SupervisedGraphStageLogic, IInHandler, IOutHandler
        {
            private readonly SkipWhile<T> _stage;

            public Logic(SkipWhile<T> stage, Attributes inheritedAttributes) : base(inheritedAttributes, stage.Shape)
            {
                _stage = stage;

                SetHandler(stage.In, this);
                SetHandler(stage.Out, this);
            }

            public void OnPush()
            {
                var element = Grab(_stage.In);
                var result = WithSupervision(() => _stage._predicate(element));
                if (result.HasValue)
                {
                    if (result.Value)
                        Pull(_stage.In);
                    else
                    {
                        Push(_stage.Out, element);
                        SetHandler(_stage.In, onPush: () => Push(_stage.Out, Grab(_stage.In)));
                    }
                }
            }

            public void OnUpstreamFinish() => CompleteStage();

            public void OnUpstreamFailure(Exception e) => FailStage(e);

            public void OnPull() => Pull(_stage.In);

            public void OnDownstreamFinish() => CompleteStage();

            protected override void OnResume(Exception ex)
            {
                if (!HasBeenPulled(_stage.In))
                    Pull(_stage.In);
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
    public abstract class SupervisedGraphStageLogic : GraphStageLogic
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
    public sealed class Collect<TIn, TOut> : GraphStage<FlowShape<TIn, TOut>>
    {
        #region Logic

        private sealed class Logic : SupervisedGraphStageLogic, IInHandler, IOutHandler
        {
            private readonly Collect<TIn, TOut> _stage;

            public Logic(Collect<TIn, TOut> stage, Attributes inheritedAttributes)
                : base(inheritedAttributes, stage.Shape)
            {
                _stage = stage;

                SetHandler(stage.In, this);
                SetHandler(stage.Out, this);
            }

            public void OnPush()
            {
                var result = WithSupervision(() => _stage._func(Grab(_stage.In)));
                if (result.HasValue)
                {
                    if (result.Value.IsDefaultForType())
                        Pull(_stage.In);
                    else
                        Push(_stage.Out, result.Value);
                }
            }

            public void OnUpstreamFinish() => CompleteStage();

            public void OnUpstreamFailure(Exception e) => FailStage(e);

            public void OnPull() => Pull(_stage.In);

            public void OnDownstreamFinish() => CompleteStage();

            protected override void OnResume(Exception ex)
            {
                if (!HasBeenPulled(_stage.In))
                    Pull(_stage.In);
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
    public sealed class Recover<T> : GraphStage<FlowShape<T, T>>
    {
        #region Logic 

        private sealed class Logic : InAndOutGraphStageLogic
        {
            private readonly Recover<T> _stage;
            private Option<T> _recovered = Option<T>.None;

            public Logic(Recover<T> stage) : base(stage.Shape)
            {
                _stage = stage;

                SetHandler(stage.In, this);
                SetHandler(stage.Out, this);
            }

            public override void OnPush() => Push(_stage.Out, Grab(_stage.In));

            public override void OnUpstreamFailure(Exception ex)
            {
                var result = _stage._recovery(ex);
                if (result.HasValue)
                {
                    if (IsAvailable(_stage.Out))
                    {
                        Push(_stage.Out, result.Value);
                        CompleteStage();
                    }
                    else
                        _recovered = result;
                }
                else
                    FailStage(ex);
            }

            public override void OnPull()
            {
                if (_recovered.HasValue)
                {
                    Push(_stage.Out, _recovered.Value);
                    CompleteStage();
                }
                else
                    Pull(_stage.In);
            }


            public override string ToString() => "RecoverLogic";
        }

        #endregion

        private readonly Func<Exception, Option<T>> _recovery;

        public Recover(Func<Exception, Option<T>> recovery)
        {
            _recovery = recovery;

            Shape = new FlowShape<T, T>(In, Out);
        }

        protected override Attributes InitialAttributes { get; } = DefaultAttributes.Recover;

        public Inlet<T> In { get; } = new Inlet<T>("Recover.in");

        public Outlet<T> Out { get; } = new Outlet<T>("Recover.out");

        public override FlowShape<T, T> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        public override string ToString() => "Recover";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    public sealed class Take<T> : SimpleLinearGraphStage<T>
    {
        #region Logic

        private sealed class Logic : InAndOutGraphStageLogic
        {
            private readonly Take<T> _stage;
            private long _left;

            public Logic(Take<T> stage) : base(stage.Shape)
            {
                _stage = stage;
                _left = stage._count;

                SetHandler(stage.Outlet, this);
                SetHandler(stage.Inlet, this);
            }

            public override void OnPush()
            {
                var leftBefore = _left;
                if (leftBefore >= 1)
                {
                    _left = leftBefore - 1;
                    Push(_stage.Outlet, Grab(_stage.Inlet));
                }

                if (leftBefore <= 1)
                    CompleteStage();
            }

            public override void OnPull()
            {
                if (_left > 0)
                    Pull(_stage.Inlet);
                else
                    CompleteStage();
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
    public sealed class Drop<T> : SimpleLinearGraphStage<T>
    {
        #region Logic

        private sealed class Logic : InAndOutGraphStageLogic
        {
            private readonly Drop<T> _stage;
            private long _left;

            public Logic(Drop<T> stage) : base(stage.Shape)
            {
                _stage = stage;
                _left = stage._count;

                SetHandler(stage.Inlet, this);
                SetHandler(stage.Outlet, this);
            }

            public override void OnPush()
            {
                if (_left > 0)
                {
                    _left--;
                    Pull(_stage.Inlet);
                }
                else
                    Push(_stage.Outlet, Grab(_stage.Inlet));
            }

            public override void OnPull() => Pull(_stage.Inlet);
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
    public sealed class Scan<TIn, TOut> : GraphStage<FlowShape<TIn, TOut>>
    {
        #region Logic 

        private sealed class Logic : InAndOutGraphStageLogic
        {
            private readonly Scan<TIn, TOut> _stage;
            private readonly Action _rest;
            private  TOut _stageAggregate;

            public Logic(Scan<TIn, TOut> stage, Attributes inheritedAttributes) : base(stage.Shape)
            {
                _stage = stage;
                _stageAggregate = stage._zero;
                var attr = inheritedAttributes.GetAttribute<ActorAttributes.SupervisionStrategy>(null);
                var decider = attr != null ? attr.Decider : Deciders.StoppingDecider;

                _rest = () =>
                {
                    try
                    {
                        _stageAggregate = stage._aggregate(_stageAggregate, Grab(stage.In));
                        Push(stage.Out, _stageAggregate);
                    }
                    catch (Exception ex)
                    {
                        switch (decider(ex))
                        {
                            case Directive.Stop:
                                FailStage(ex);
                                break;
                            case Directive.Resume:
                                if (!HasBeenPulled(stage.In))
                                    Pull(stage.In);
                                break;
                            case Directive.Restart:
                                _stageAggregate = stage._zero;
                                Push(stage.Out, _stageAggregate);
                                break;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }
                    }
                };
                
                SetHandler(stage.In, this);
                SetHandler(stage.Out, this);
            }

            public override void OnPush()
            {
                // Initial behavior makes sure that the zero gets flushed if upstream is empty
            }

            public override void OnUpstreamFinish()
            {
                SetHandler(_stage.Out, onPull: () =>
                {
                    Push(_stage.Out, _stageAggregate);
                    CompleteStage();
                });
            }

            public override void OnPull()
            {
                Push(_stage.Out, _stageAggregate);
                SetHandler(_stage.Out, onPull: () => Pull(_stage.In));
                SetHandler(_stage.In, onPush: _rest);
            }
        }

        #endregion

        private readonly Func<TOut, TIn, TOut> _aggregate;
        private readonly TOut _zero;

        public Scan(TOut zero, Func<TOut, TIn, TOut> aggregate)
        {
            _zero = zero;
            _aggregate = aggregate;

            Shape = new FlowShape<TIn, TOut>(In, Out);
        }

        protected override Attributes InitialAttributes { get; } = DefaultAttributes.Scan;

        public Inlet<TIn> In { get; } = new Inlet<TIn>("Scan.in");

        public Outlet<TOut> Out { get; } = new Outlet<TOut>("Scan.out");

        public override FlowShape<TIn, TOut> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            => new Logic(this, inheritedAttributes);

        public override string ToString() => "Scan";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    public sealed class Aggregate<TIn, TOut> : GraphStage<FlowShape<TIn, TOut>>
    {
        #region Logic

        private sealed class Logic : InAndOutGraphStageLogic
        {
            private readonly Aggregate<TIn, TOut> _stage;
            private readonly Decider _decider;
            private TOut _aggregator;

            public Logic(Aggregate<TIn, TOut> stage, Attributes inheritedAttributes) : base(stage.Shape)
            {
                _stage = stage;
                _aggregator = stage._zero;

                var attr = inheritedAttributes.GetAttribute<ActorAttributes.SupervisionStrategy>(null);
                _decider = attr != null ? attr.Decider : Deciders.StoppingDecider;

                SetHandler(stage.In, this);
                SetHandler(stage.Out, this);
            }

            public override void OnPush()
            {
                try
                {
                    _aggregator = _stage._aggregate(_aggregator, Grab(_stage.In));
                    Pull(_stage.In);
                }
                catch (Exception ex)
                {
                    if (_decider(ex) == Directive.Stop)
                        FailStage(ex);
                    else
                    {
                        _aggregator = _stage._zero;
                        Pull(_stage.In);
                    }
                }
            }

            public override void OnUpstreamFinish()
            {
                if (IsAvailable(_stage.Out))
                {
                    Push(_stage.Out, _aggregator);
                    CompleteStage();
                }
            }

            public override void OnPull()
            {
                if (IsClosed(_stage.In))
                {
                    Push(_stage.Out, _aggregator);
                    CompleteStage();
                }
                else
                    Pull(_stage.In);
            }
        }

        #endregion

        private readonly TOut _zero;
        private readonly Func<TOut, TIn, TOut> _aggregate;

        public Aggregate(TOut zero, Func<TOut, TIn, TOut> aggregate)
        {
            _zero = zero;
            _aggregate = aggregate;

            Shape = new FlowShape<TIn, TOut>(In, Out);
        }

        protected override Attributes InitialAttributes { get; } = DefaultAttributes.Aggregate;

        public Inlet<TIn> In { get; } = new Inlet<TIn>("Aggregate.in");

        public Outlet<TOut> Out { get; } = new Outlet<TOut>("Aggregate.out");

        public override FlowShape<TIn, TOut> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            => new Logic(this, inheritedAttributes);

        public override string ToString() => "Aggregate";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    public sealed class AggregateAsync<TIn, TOut> : GraphStage<FlowShape<TIn, TOut>>
    {
        #region Logic

        private sealed class Logic : InAndOutGraphStageLogic
        {
            private readonly AggregateAsync<TIn, TOut> _stage;
            private readonly Decider _decider;
            private readonly Action<Result<TOut>> _taskCallback;
            private TOut _aggregator;
            private Task<TOut> _aggregating;
            

            public Logic(AggregateAsync<TIn, TOut> stage, Attributes inheritedAttributes) : base(stage.Shape)
            {
                _stage = stage;
                _aggregator = stage._zero;
                _aggregating = Task.FromResult(_aggregator);

                var attr = inheritedAttributes.GetAttribute<ActorAttributes.SupervisionStrategy>(null);
                _decider = attr != null ? attr.Decider : Deciders.StoppingDecider;

                _taskCallback = GetAsyncCallback<Result<TOut>>(result =>
                {
                    if (result.IsSuccess && result.Value != null)
                    {
                        var update = result.Value;
                        _aggregator = update;

                        if (IsClosed(stage.In))
                        {
                            Push(stage.Out, update);
                            CompleteStage();
                        }
                        else if (IsAvailable(stage.Out) && !HasBeenPulled(stage.In))
                            TryPull(stage.In);
                    }
                    else
                    {
                        var ex = !result.IsSuccess
                            ? result.Exception
                            : ReactiveStreamsCompliance.ElementMustNotBeNullException;

                        var supervision = _decider(ex);
                        if (supervision == Directive.Stop)
                            FailStage(ex);
                        else
                        {
                            if(supervision == Directive.Restart)
                                OnRestart();

                            if (IsClosed(stage.In))
                            {
                                Push(stage.Out, _aggregator);
                                CompleteStage();
                            }
                            else if (IsAvailable(stage.Out) && !HasBeenPulled(stage.In))
                                TryPull(stage.In);
                        }
                    }
                });

                SetHandler(stage.In, this);
                SetHandler(stage.Out, this);
            }

            public override void OnPush()
            {
                try
                {
                    _aggregating = _stage._aggregate(_aggregator, Grab(_stage.In));
                    if (_aggregating.IsCompleted)
                        _taskCallback(Result.FromTask(_aggregating));
                    else
                        _aggregating.ContinueWith(t => _taskCallback(Result.FromTask(t)),
                            TaskContinuationOptions.ExecuteSynchronously);

                }
                catch (Exception ex)
                {
                    var supervision = _decider(ex);
                    if (supervision == Directive.Stop)
                    {
                        FailStage(ex);
                        return;
                    }

                    if (supervision == Directive.Restart)
                        OnRestart();

                    // just ignore on Resume

                    TryPull(_stage.In);
                }
            }

            public override void OnUpstreamFinish()
            {
            }

            public override void OnPull()
            {
                if(!HasBeenPulled(_stage.In))
                    TryPull(_stage.In);
            }

            private void OnRestart() => _aggregator = _stage._zero;

            public override string ToString() => $"AggregateAsync.Logic(completed={_aggregating.IsCompleted})";
        }

        #endregion

        private readonly TOut _zero;
        private readonly Func<TOut, TIn, Task<TOut>> _aggregate;

        public AggregateAsync(TOut zero, Func<TOut, TIn, Task<TOut>> aggregate)
        {
            _zero = zero;
            _aggregate = aggregate;

            Shape = new FlowShape<TIn, TOut>(In, Out);
        }

        protected override Attributes InitialAttributes { get; } = DefaultAttributes.AggregateAsync;

        public Inlet<TIn> In { get; } = new Inlet<TIn>("AggregateAsync.in");

        public Outlet<TOut> Out { get; } = new Outlet<TOut>("AggregateAsync.out");

        public override FlowShape<TIn, TOut> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            => new Logic(this, inheritedAttributes);

        public override string ToString() => "AggregateAsync";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    public sealed class Intersperse<T> : GraphStage<FlowShape<T, T>>
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
                if (_stage.InjectStartEnd)
                    _logic.EmitMultiple(_stage.Out, new[] {_stage._start, _logic.Grab(_stage.In)});
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

            public override void OnPush()
                => _logic.EmitMultiple(_stage.Out, new[] {_stage._inject, _logic.Grab(_stage.In)});

            public override void OnUpstreamFinish()
            {
                if (_stage.InjectStartEnd) _logic.Emit(_stage.Out, _stage._end);
                _logic.CompleteStage();
            }
        }

        private sealed class Logic : GraphStageLogic, IOutHandler
        {
            private readonly Intersperse<T> _stage;

            public Logic(Intersperse<T> stage) : base(stage.Shape)
            {
                _stage = stage;
                SetHandler(stage.In, new StartInHandler(stage, this));
                SetHandler(stage.Out, this);
            }

            public void OnPull() => Pull(_stage.In);

            public void OnDownstreamFinish() => CompleteStage();
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
    public sealed class Grouped<T> : GraphStage<FlowShape<T, IEnumerable<T>>>
    {
        #region Logic

        private sealed class Logic : InAndOutGraphStageLogic
        {
            private readonly Grouped<T> _stage;
            private List<T> _buffer;
            private int _left;

            public Logic(Grouped<T> stage) : base(stage.Shape)
            {
                _stage = stage;
                _buffer = new List<T>(stage._count);
                _left = stage._count;

                SetHandler(stage.In, this);
                SetHandler(stage.Out, this);
            }

            public override void OnPush()
            {
                _buffer.Add(Grab(_stage.In));
                _left--;

                if (_left == 0)
                    PushAndClearBuffer();
                else
                    Pull(_stage.In);
            }

            public override void OnUpstreamFinish()
            {
                // This means the buf is filled with some elements but not enough (left < n) to group together.
                // Since the upstream has finished we have to push them to downstream though.
                if (_left < _stage._count)
                    PushAndClearBuffer();

                CompleteStage();
            }

            public override void OnPull() => Pull(_stage.In);

            private void PushAndClearBuffer()
            {
                var elements = _buffer;
                _buffer = new List<T>(_stage._count);
                _left = _stage._count;
                Push(_stage.Out, elements);
            }
        }

        #endregion

        private readonly int _count;

        public Grouped(int count)
        {
            if (count <= 0)
                throw new ArgumentException("count must be greater than 0", nameof(count));

            _count = count;

            Shape = new FlowShape<T, IEnumerable<T>>(In, Out);
        }

        protected override Attributes InitialAttributes { get; } = DefaultAttributes.Grouped;

        public Inlet<T> In { get; } = new Inlet<T>("Grouped.in");

        public Outlet<IEnumerable<T>> Out { get; } = new Outlet<IEnumerable<T>>("Grouped.out");

        public override FlowShape<T, IEnumerable<T>> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        public override string ToString() => "Grouped";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    public sealed class LimitWeighted<T> : GraphStage<FlowShape<T, T>>
    {
        #region Logic

        private sealed class Logic : SupervisedGraphStageLogic, IInHandler, IOutHandler
        {
            private readonly LimitWeighted<T> _stage;
            private long _left;

            public Logic(LimitWeighted<T> stage, Attributes inheritedAttributes) : base(inheritedAttributes, stage.Shape)
            {
                _stage = stage;
                _left = stage._max;

                SetHandler(stage.In, this);
                SetHandler(stage.Out, this);
            }

            public void OnPush()
            {
                var element = Grab(_stage.In);
                var result = WithSupervision(() => _stage._costFunc(element));
                if (result.HasValue)
                {
                    _left -= result.Value;
                    if (_left >= 0)
                        Push(_stage.Out, element);
                    else
                        FailStage(new StreamLimitReachedException(_stage._max));
                }
            }

            public void OnUpstreamFinish() => CompleteStage();

            public void OnUpstreamFailure(Exception e) => FailStage(e);

            public void OnPull() => Pull(_stage.In);

            public void OnDownstreamFinish() => CompleteStage();

            protected override void OnResume(Exception ex) => TryPull();

            protected override void OnRestart(Exception ex)
            {
                _left = _stage._max;
                TryPull();
            }

            private void TryPull()
            {
                if (!HasBeenPulled(_stage.In))
                    Pull(_stage.In);
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
    public sealed class Sliding<T> : GraphStage<FlowShape<T, IEnumerable<T>>>
    {
        #region Logic

        private sealed class Logic : InAndOutGraphStageLogic
        {
            private readonly Sliding<T> _stage;
            private IImmutableList<T> _buffer;

            public Logic(Sliding<T> stage) : base(stage.Shape)
            {
                _stage = stage;
                _buffer = ImmutableList<T>.Empty;

                SetHandler(stage.In, this);
                SetHandler(stage.Out, this);
            }

            public override void OnPush()
            {
                _buffer = _buffer.Add(Grab(_stage.In));
                if (_buffer.Count < _stage._count)
                    Pull(_stage.In);
                else if (_buffer.Count == _stage._count)
                    Push(_stage.Out, _buffer);
                else if (_stage._step <= _stage._count)
                {
                    _buffer = _buffer.Drop(_stage._step).ToImmutableList();
                    if (_buffer.Count == _stage._count)
                        Push(_stage.Out, _buffer);
                    else
                        Pull(_stage.In);
                }
                else if (_stage._step > _stage._count)
                {
                    if (_buffer.Count == _stage._step)
                        _buffer = _buffer.Drop(_stage._step).ToImmutableList();
                    Pull(_stage.In);
                }
            }

            public override void OnUpstreamFinish()
            {
                // We can finish current _stage directly if:
                //  1. the buf is empty or
                //  2. when the step size is greater than the sliding size (step > n) and current _stage is in between
                //     two sliding (ie. buf.size >= n && buf.size < step).
                // Otherwise it means there is still a not finished sliding so we have to push them before finish current _stage.
                if (_buffer.Count < _stage._count && _buffer.Count > 0)
                    Push(_stage.Out, _buffer);

                CompleteStage();
            }

            public override void OnPull() => Pull(_stage.In);
        }

        #endregion

        private readonly int _count;
        private readonly int _step;

        public Sliding(int count, int step)
        {
            if (count <= 0)
                throw new ArgumentException("count must be greater than 0", nameof(count));
            if (step <= 0)
                throw new ArgumentException("step must be greater than 0", nameof(step));

            _count = count;
            _step = step;

            Shape = new FlowShape<T, IEnumerable<T>>(In, Out);
        }

        protected override Attributes InitialAttributes { get; } = DefaultAttributes.Sliding;

        public Inlet<T> In { get; } = new Inlet<T>("Sliding.in");

        public Outlet<IEnumerable<T>> Out { get; } = new Outlet<IEnumerable<T>>("Sliding.out");

        public override FlowShape<T, IEnumerable<T>> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        public override string ToString() => "Sliding";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    public sealed class Buffer<T> : DetachedStage<T, T>
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
            => _buffer = Buffer.Create<T>(_count, context.Materializer);

        public override IUpstreamDirective OnPush(T element, IDetachedContext<T> context)
            => context.IsHoldingDownstream ? context.PushAndPull(element) : _enqueueAction(context, element);

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

        public override ITerminationDirective OnUpstreamFinish(IDetachedContext<T> context)
            => _buffer.IsEmpty ? context.Finish() : context.AbsorbTermination();

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
                            return
                                context.Fail(new BufferOverflowException($"Buffer overflow (max capacity was {_count})"));
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
    public sealed class OnCompleted<TIn, TOut> : PushStage<TIn, TOut>
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
    public sealed class Batch<TIn, TOut> : GraphStage<FlowShape<TIn, TOut>>
    {
        #region internal classes

        private sealed class Logic : InAndOutGraphStageLogic
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

                SetHandler(_shape.Inlet, this);
                SetHandler(_shape.Outlet, this);
            }

            public override void OnPush()
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
            }

            public override void OnUpstreamFinish()
            {
                if (!_aggregate.HasValue)
                    CompleteStage();
            }

            public override void OnPull()
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

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            => new Logic(inheritedAttributes, this);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    public sealed class Expand<TIn, TOut> : GraphStage<FlowShape<TIn, TOut>>
    {
        #region internal classes

        private sealed class Logic : InAndOutGraphStageLogic
        {
            private readonly Expand<TIn, TOut> _stage;
            private IIterator<TOut> _iterator;
            private bool _expanded;

            public Logic(Expand<TIn, TOut> stage) : base(stage.Shape)
            {
                _stage = stage;
                _iterator = new IteratorAdapter<TOut>(Enumerable.Empty<TOut>().GetEnumerator());

                SetHandler(stage.In, this);
                SetHandler(stage.Out, this);
            }

            public override void PreStart() => Pull(_stage.In);

            public override void OnPush()
            {
                _iterator = new IteratorAdapter<TOut>(_stage._extrapolate(Grab(_stage.In)));
                if (_iterator.HasNext())
                {
                    if (IsAvailable(_stage.Out))
                    {
                        _expanded = true;
                        Pull(_stage.In);
                        Push(_stage.Out, _iterator.Next());
                    }
                    else
                        _expanded = false;
                }
                else
                    Pull(_stage.In);
            }

            public override void OnUpstreamFinish()
            {
                if (_iterator.HasNext() && !_expanded)
                {
                    // need to wait
                }
                else
                    CompleteStage();
            }

            public override void OnPull()
            {
                if (_iterator.HasNext())
                {
                    if (!_expanded)
                    {
                        _expanded = true;
                        if (IsClosed(_stage.In))
                        {
                            Push(_stage.Out, _iterator.Next());
                            CompleteStage();
                        }
                        else
                        {
                            // expand needs to pull first to be "fair" when upstream is not actually slow
                            Pull(_stage.In);
                            Push(_stage.Out, _iterator.Next());
                        }
                    }
                    else
                        Push(_stage.Out, _iterator.Next());
                }
            }
        }

        #endregion

        private readonly Func<TIn, IEnumerator<TOut>> _extrapolate;

        public Expand(Func<TIn, IEnumerator<TOut>> extrapolate)
        {
            _extrapolate = extrapolate;

            Shape = new FlowShape<TIn, TOut>(In, Out);
        }

        protected override Attributes InitialAttributes => DefaultAttributes.Expand;

        public Inlet<TIn> In { get; } = new Inlet<TIn>("expand.in");

        public Outlet<TOut> Out { get; } = new Outlet<TOut>("expand.out");

        public override FlowShape<TIn, TOut> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        public override string ToString() => "Expand";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    public sealed class SelectAsync<TIn, TOut> : GraphStage<FlowShape<TIn, TOut>>
    {
        #region internal classes


        private sealed class Logic : InAndOutGraphStageLogic
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

                public void SetElement(Result<T> result)
                {
                    Element = result.IsSuccess && result.Value == null
                        ? Result.Failure<T>(ReactiveStreamsCompliance.ElementMustNotBeNullException)
                        : result;
                }

                public void Invoke(Result<T> result)
                {
                    SetElement(result);
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

                _taskCallback = GetAsyncCallback<Holder<TOut>>(HolderCompleted);

                SetHandler(stage.In, this);
                SetHandler(stage.Out, this);
            }

            public override void OnPush()
            {
                try
                {
                    var task = _stage._mapFunc(Grab(_stage.In));
                    var holder = new Holder<TOut>(NotYetThere, _taskCallback);
                    _buffer.Enqueue(holder);

                    // We dispatch the task if it's ready to optimize away
                    // scheduling it to an execution context
                    if (task.IsCompleted)
                    {
                        holder.SetElement(Result.FromTask(task));
                        HolderCompleted(holder);
                    }
                    else
                        task.ContinueWith(t => holder.Invoke(Result.FromTask(t)),
                            TaskContinuationOptions.ExecuteSynchronously);
                }
                catch (Exception e)
                {
                    if (_decider(e) == Directive.Stop)
                        FailStage(e);
                }
                if (Todo < _stage._parallelism && !HasBeenPulled(_stage.In))
                    TryPull(_stage.In);
            }

            public override void OnUpstreamFinish()
            {
                if (Todo == 0)
                    CompleteStage();
            }

            public override void OnPull() => PushOne();

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

            private void HolderCompleted(Holder<TOut> holder)
            {
                var element = holder.Element;
                if(!element.IsSuccess && _decider(element.Exception) == Directive.Stop)
                    FailStage(element.Exception);
                else if (IsAvailable(_stage.Out))
                    PushOne();
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

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            => new Logic(inheritedAttributes, this);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    public sealed class SelectAsyncUnordered<TIn, TOut> : GraphStage<FlowShape<TIn, TOut>>
    {
        #region internal classes

        private sealed class Logic : InAndOutGraphStageLogic
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

                _taskCallback = GetAsyncCallback<Result<TOut>>(TaskCompleted);

                SetHandler(_stage.In, this);
                SetHandler(_stage.Out, this);
            }

            public override void OnPush()
            {
                try
                {
                    var task = _stage._mapFunc(Grab(_stage.In));
                    _inFlight++;

                    if (task.IsCompleted)
                        TaskCompleted(Result.FromTask(task));
                    else
                        task.ContinueWith(t => _taskCallback(Result.FromTask(t)),
                            TaskContinuationOptions.ExecuteSynchronously);
                }
                catch (Exception e)
                {
                    if (_decider(e) == Directive.Stop)
                        FailStage(e);
                }

                if (Todo < _stage._parallelism && !HasBeenPulled(_stage.In))
                    TryPull(_stage.In);
            }

            public override void OnUpstreamFinish()
            {
                if (Todo == 0)
                    CompleteStage();
            }

            public override void OnPull()
            {
                var inlet = _stage.In;
                if (!_buffer.IsEmpty)
                    Push(_stage.Out, _buffer.Dequeue());
                else if (IsClosed(inlet) && Todo == 0)
                    CompleteStage();

                if (Todo < _stage._parallelism && !HasBeenPulled(inlet))
                    TryPull(inlet);
            }

            private void TaskCompleted(Result<TOut> result)
            {
                _inFlight--;
                if (result.IsSuccess && result.Value != null)
                {
                    if (IsAvailable(_stage.Out))
                    {
                        if (!HasBeenPulled(_stage.In))
                            TryPull(_stage.In);
                        Push(_stage.Out, result.Value);
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
                    else if (IsClosed(_stage.In) && Todo == 0)
                        CompleteStage();
                    else if (!HasBeenPulled(_stage.In))
                        TryPull(_stage.In);
                }
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

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            => new Logic(inheritedAttributes, this);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    public sealed class Log<T> : SimpleLinearGraphStage<T>
    {
        private static readonly Attributes.LogLevels DefaultLogLevels = new Attributes.LogLevels(
            onElement: LogLevel.DebugLevel,
            onFinish: LogLevel.DebugLevel,
            onFailure: LogLevel.ErrorLevel);

        #region Logic

        private sealed class Logic : InAndOutGraphStageLogic
        {
            private readonly Log<T> _stage;
            private readonly Attributes _inheritedAttributes;
            private readonly Decider _decider;
            private Attributes.LogLevels _logLevels;
            private ILoggingAdapter _log;

            public Logic(Log<T> stage, Attributes inheritedAttributes) : base(stage.Shape)
            {
                _stage = stage;
                _inheritedAttributes = inheritedAttributes;
                _decider =
                    inheritedAttributes.GetAttribute(new ActorAttributes.SupervisionStrategy(Deciders.StoppingDecider))
                        .Decider;

                SetHandler(stage.Inlet, this);
                SetHandler(stage.Outlet, this);
            }

            public override void OnPush()
            {
                try
                {
                    var element = Grab(_stage.Inlet);
                    if (IsEnabled(_logLevels.OnElement))
                        _log.Log(_logLevels.OnElement, $"[{_stage._name}] Element: {_stage._extract(element)}");

                    Push(_stage.Outlet, element);
                }
                catch (Exception ex)
                {
                    if (_decider(ex) == Directive.Stop)
                        FailStage(ex);
                    else
                        Pull(_stage.Inlet);
                }
            }

            public override void OnUpstreamFinish()
            {
                if (IsEnabled(_logLevels.OnFinish))
                    _log.Log(_logLevels.OnFinish, $"[{_stage._name}] Upstream finished.");

                CompleteStage();
            }

            public override void OnUpstreamFailure(Exception ex)
            {
                if (IsEnabled(_logLevels.OnFailure))
                {
                    if (_logLevels.OnFailure == LogLevel.ErrorLevel)
                        _log.Error(ex, $"[{_stage._name}] Upstream failed.");
                    else
                        _log.Log(_logLevels.OnFailure,
                            $"[{_stage._name}] Upstream failed, cause: {ex.GetType()} {ex.Message}");
                }

                FailStage(ex);
            }

            public override void OnPull() => Pull(_stage.Inlet);

            public override void OnDownstreamFinish()
            {
                if (IsEnabled(_logLevels.OnFinish))
                    _log.Log(_logLevels.OnFinish, $"[{_stage._name}] Downstream finished.");

                CompleteStage();
            }

            public override void PreStart()
            {
                _logLevels = _inheritedAttributes.GetAttribute(DefaultLogLevels);
                if (_stage._adapter != null)
                    _log = _stage._adapter;
                else
                {
                    try
                    {
                        var materializer = ActorMaterializerHelper.Downcast(Materializer);
                        _log = new BusLogging(materializer.System.EventStream, _stage._name, GetType(), new DefaultLogMessageFormatter());
                    }
                    catch (Exception ex)
                    {
                        throw new Exception(
                            "Log stage can only provide LoggingAdapter when used with ActorMaterializer! Provide a LoggingAdapter explicitly or use the actor based flow materializer.",
                            ex);
                    }
                }
            }

            private bool IsEnabled(LogLevel level) => level != Attributes.LogLevels.Off;
        }

        #endregion

        private readonly string _name;
        private readonly Func<T, object> _extract;
        private readonly ILoggingAdapter _adapter;

        public Log(string name, Func<T, object> extract, ILoggingAdapter adapter)
        {
            _name = name;
            _extract = extract;
            _adapter = adapter;
        }

        // TODO more optimisations can be done here - prepare logOnPush function etc
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            => new Logic(this, inheritedAttributes);

        public override string ToString() => "Log";
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
    public sealed class GroupedWithin<T> : GraphStage<FlowShape<T, IEnumerable<T>>>
    {
        #region internal classes

        private sealed class Logic : TimerGraphStageLogic, IInHandler, IOutHandler
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

                SetHandler(_stage._in, this);
                SetHandler(_stage._out, this);
            }

            public void OnPush()
            {
                if (!_groupClosed)
                    NextElement(Grab(_stage._in)); // otherwise keep the element for next round
            }

            public void OnUpstreamFinish()
            {
                _finished = true;
                if (_groupEmitted)
                    CompleteStage();
                else
                    CloseGroup();
            }

            public void OnUpstreamFailure(Exception e) => FailStage(e);

            public void OnPull()
            {
                if (_groupClosed)
                    EmitGroup();
            }

            public void OnDownstreamFinish() => CompleteStage();

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
    public sealed class Delay<T> : SimpleLinearGraphStage<T>
    {
        #region internal classes

        private sealed class Logic : TimerGraphStageLogic, IInHandler, IOutHandler
        {
            private const string TimerName = "DelayedTimer";
            private readonly Delay<T> _stage;
            private IBuffer<Tuple<long, T>> _buffer; // buffer has pairs timestamp with upstream element
            private bool _willStop;
            private readonly int _size;
            private readonly Action _overflowStrategy;

            public Logic(Attributes inheritedAttributes, Delay<T> stage) : base(stage.Shape)
            {
                var inputBuffer = inheritedAttributes.GetAttribute<Attributes.InputBuffer>(null);
                if (inputBuffer == null)
                    throw new IllegalStateException($"Couldn't find InputBuffer Attribute for {this}");

                _stage = stage;
                _size = inputBuffer.Max;
                _overflowStrategy = OnPushStrategy(_stage._strategy);

                SetHandler(_stage.Inlet, this);
                SetHandler(_stage.Outlet, this);
            }

            public void OnPush()
            {
                if (_buffer.IsFull)
                    _overflowStrategy();
                else
                {
                    GrabAndPull(_stage._strategy != DelayOverflowStrategy.Backpressure || _buffer.Used < _size - 1);
                    if (!IsTimerActive(TimerName))
                        ScheduleOnce(TimerName, _stage._delay);
                }
            }

            public void OnUpstreamFinish()
            {
                if (IsAvailable(_stage.Outlet) && IsTimerActive(TimerName))
                    _willStop = true;
                else
                    CompleteStage();
            }

            public void OnUpstreamFailure(Exception e) => FailStage(e);

            public void OnPull()
            {
                if (!IsTimerActive(TimerName) && !_buffer.IsEmpty && NextElementWaitTime < 0)
                    Push(_stage.Outlet, _buffer.Dequeue().Item2);

                if (!_willStop && !HasBeenPulled(_stage.Inlet))
                    Pull(_stage.Inlet);
                CompleteIfReady();
            }

            public void OnDownstreamFinish() => CompleteStage();

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
    public sealed class TakeWithin<T> : SimpleLinearGraphStage<T>
    {
        #region internal class

        private sealed class Logic : TimerGraphStageLogic, IInHandler, IOutHandler
        {
            private readonly TakeWithin<T> _stage;

            public Logic(TakeWithin<T> stage) : base(stage.Shape)
            {
                _stage = stage;

                SetHandler(stage.Inlet, this);
                SetHandler(stage.Outlet, this);
            }

            public void OnPush() => Push(_stage.Outlet, Grab(_stage.Inlet));

            public void OnUpstreamFinish() => CompleteStage();

            public void OnUpstreamFailure(Exception e) => FailStage(e);

            public void OnPull() => Pull(_stage.Inlet);

            public void OnDownstreamFinish() => CompleteStage();

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
    public sealed class SkipWithin<T> : SimpleLinearGraphStage<T>
    {
        private readonly TimeSpan _timeout;

        #region internal classes

        private sealed class Logic : TimerGraphStageLogic, IInHandler, IOutHandler
        {
            private readonly SkipWithin<T> _stage;
            private bool _allow;

            public Logic(SkipWithin<T> stage) : base(stage.Shape)
            {
                _stage = stage;

                SetHandler(stage.Inlet, this);
                SetHandler(stage.Outlet, this);
            }

            public void OnPush()
            {
                if (_allow)
                    Push(_stage.Outlet, Grab(_stage.Inlet));
                else
                    Pull(_stage.Inlet);
            }

            public void OnUpstreamFinish() => CompleteStage();

            public void OnUpstreamFailure(Exception e) => FailStage(e);

            public void OnPull() => Pull(_stage.Inlet);

            public void OnDownstreamFinish() => CompleteStage();

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
    public sealed class Sum<T> : SimpleLinearGraphStage<T>
    {
        #region internal classes

        private sealed class Logic : InAndOutGraphStageLogic
        {
            private readonly Sum<T> _stage;
            private T _aggregator;
            private readonly LambdaInHandler _rest;

            public Logic(Sum<T> stage) : base(stage.Shape)
            {
                _stage = stage;
                _rest = new LambdaInHandler(onPush: () =>
                {
                    _aggregator = stage._reduce(_aggregator, Grab(stage.Inlet));
                    Pull(stage.Inlet);
                }, onUpstreamFinish: () =>
                {
                    Push(stage.Outlet, _aggregator);
                    CompleteStage();
                });

                // Initial input handler
                SetHandler(stage.Inlet, this);
                SetHandler(stage.Outlet, this);
            }

            public override void OnPush()
            {
                _aggregator = Grab(_stage.Inlet);
                Pull(_stage.Inlet);
                SetHandler(_stage.Inlet, _rest);
            }

            public override void OnUpstreamFinish() => FailStage(new NoSuchElementException("sum over empty stream"));

            public override void OnPull() => Pull(_stage.Inlet);

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
    public sealed class RecoverWith<TOut, TMat> : SimpleLinearGraphStage<TOut>
    {
        #region internal classes

        private sealed class Logic : InAndOutGraphStageLogic
        {
            private const int InfiniteRetries = -1;
            private readonly RecoverWith<TOut, TMat> _stage;
            private int _attempt;

            public Logic(RecoverWith<TOut, TMat> stage) : base(stage.Shape)
            {
                _stage = stage;

                SetHandler(stage.Outlet, this);
                SetHandler(stage.Inlet, this);
            }

            public override void OnPush() => Push(_stage.Outlet, Grab(_stage.Inlet));

            public override void OnUpstreamFailure(Exception e) => OnFailure(e);

            public override void OnPull() => Pull(_stage.Inlet);

            private void OnFailure(Exception ex)
            {
                var result = _stage._partialFunction(ex);
                if (result != null &&
                    (_stage._maximumRetries == InfiniteRetries || _attempt < _stage._maximumRetries))
                {
                    SwitchTo(result);
                    _attempt++;
                }
                else
                    FailStage(ex);
            }

            private void SwitchTo(IGraph<SourceShape<TOut>, TMat> source)
            {
                var sinkIn = new SubSinkInlet<TOut>(this, "RecoverWithSink");
                sinkIn.SetHandler(new LambdaInHandler(onPush: () =>
                {
                    if (IsAvailable(_stage.Outlet))
                    {
                        Push(_stage.Outlet, sinkIn.Grab());
                        sinkIn.Pull();
                    }
                }, onUpstreamFinish: () =>
                {
                    if (!sinkIn.IsAvailable)
                        CompleteStage();
                }, onUpstreamFailure: OnFailure));

                Action pushOut = () =>
                {
                    Push(_stage.Outlet, sinkIn.Grab());
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
                SetHandler(_stage.Outlet, outHandler);
                sinkIn.Pull();
            }
        }

        #endregion

        private readonly Func<Exception, IGraph<SourceShape<TOut>, TMat>> _partialFunction;
        private readonly int _maximumRetries;

        public RecoverWith(Func<Exception, IGraph<SourceShape<TOut>, TMat>> partialFunction, int maximumRetries)
        {
            if (maximumRetries < -1)
                throw new ArgumentException("number of retries must be non-negative or equal to -1",
                    nameof(maximumRetries));

            _partialFunction = partialFunction;
            _maximumRetries = maximumRetries;
        }

        protected override Attributes InitialAttributes { get; } = DefaultAttributes.RecoverWith;

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        public override string ToString() => "RecoverWith";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    public sealed class StatefulSelectMany<TIn, TOut> : GraphStage<FlowShape<TIn, TOut>>
    {
        #region internal classes

        private sealed class Logic : InAndOutGraphStageLogic
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

                SetHandler(stage._in, this);
                SetHandler(stage._out, this);
            }

            public override void OnPush()
            {
                try
                {
                    _currentIterator = new IteratorAdapter<TOut>(_plainConcat(Grab(_stage._in)).GetEnumerator());
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
            }

            public override void OnUpstreamFinish()
            {
                if (!HasNext)
                    CompleteStage();
            }

            public override void OnPull() => PushPull();

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
