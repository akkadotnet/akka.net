//-----------------------------------------------------------------------
// <copyright file="Ops.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Annotations;
using Akka.Event;
using Akka.Pattern;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation.Stages;
using Akka.Streams.Stage;
using Akka.Streams.Supervision;
using Akka.Streams.Util;
using Akka.Util;
using Akka.Util.Internal;
using Decider = Akka.Streams.Supervision.Decider;
using Directive = Akka.Streams.Supervision.Directive;

namespace Akka.Streams.Implementation.Fusing
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    [InternalApi]
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

                SetHandler(stage.In, stage.Out, this);
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="func">TBD</param>
        public Select(Func<TIn, TOut> func)
        {
            _func = func;

            Shape = new FlowShape<TIn, TOut>(In, Out);
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.Select;

        /// <summary>
        /// TBD
        /// </summary>
        public Inlet<TIn> In { get; } = new Inlet<TIn>("Select.in");

        /// <summary>
        /// TBD
        /// </summary>
        public Outlet<TOut> Out { get; } = new Outlet<TOut>("Select.out");

        /// <summary>
        /// TBD
        /// </summary>
        public override FlowShape<TIn, TOut> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            => new Logic(this, inheritedAttributes);

        /// <summary>
        /// Returns a <see cref="string" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="string" /> that represents this instance.
        /// </returns>
        public override string ToString() => "Select";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    [InternalApi]
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="predicate">TBD</param>
        public Where(Predicate<T> predicate)
        {
            _predicate = predicate;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            => new Logic(this, inheritedAttributes);

        /// <summary>
        /// Returns a <see cref="string" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="string" /> that represents this instance.
        /// </returns>
        public override string ToString() => "Where";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    [InternalApi]
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
                    {
                        if (_stage._inclusive)
                            Push(_stage.Outlet, element);

                        CompleteStage();
                    }
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
        private readonly bool _inclusive;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="predicate">TBD</param>
        /// <param name="inclusive">TBD</param>
        public TakeWhile(Predicate<T> predicate, bool inclusive)
        {
            _inclusive = inclusive;
            _predicate = predicate;
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.TakeWhile;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            => new Logic(this, inheritedAttributes);

        /// <summary>
        /// Returns a <see cref="string" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="string" /> that represents this instance.
        /// </returns>
        public override string ToString() => "TakeWhile";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    [InternalApi]
    public sealed class SkipWhile<T> : SimpleLinearGraphStage<T>
    {
        #region Logic

        private sealed class Logic : SupervisedGraphStageLogic, IInHandler, IOutHandler
        {
            private readonly SkipWhile<T> _stage;

            public Logic(SkipWhile<T> stage, Attributes inheritedAttributes) : base(inheritedAttributes, stage.Shape)
            {
                _stage = stage;

                SetHandler(stage.Inlet, this);
                SetHandler(stage.Outlet, this);
            }

            public void OnPush()
            {
                var element = Grab(_stage.Inlet);
                var result = WithSupervision(() => _stage._predicate(element));
                if (result.HasValue)
                {
                    if (result.Value)
                        Pull(_stage.Inlet);
                    else
                    {
                        Push(_stage.Outlet, element);
                        SetHandler(_stage.Inlet, onPush: () => Push(_stage.Outlet, Grab(_stage.Inlet)));
                    }
                }
            }

            public void OnUpstreamFinish() => CompleteStage();

            public void OnUpstreamFailure(Exception e) => FailStage(e);

            public void OnPull() => Pull(_stage.Inlet);

            public void OnDownstreamFinish() => CompleteStage();

            protected override void OnResume(Exception ex)
            {
                if (!HasBeenPulled(_stage.Inlet))
                    Pull(_stage.Inlet);
            }
        }

        #endregion

        private readonly Predicate<T> _predicate;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="predicate">TBD</param>
        public SkipWhile(Predicate<T> predicate) : base("SkipWhile")
        {
            _predicate = predicate;
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.SkipWhile;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            => new Logic(this, inheritedAttributes);

        /// <summary>
        /// Returns a <see cref="string" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="string" /> that represents this instance.
        /// </returns>
        public override string ToString() => "SkipWhile";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    [InternalApi]
    public abstract class SupervisedGraphStageLogic : GraphStageLogic
    {
        private readonly Lazy<Decider> _decider;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <param name="shape">TBD</param>
        protected SupervisedGraphStageLogic(Attributes inheritedAttributes, Shape shape) : base(shape)
        {
            _decider = new Lazy<Decider>(() =>
            {
                var attr = inheritedAttributes.GetAttribute<ActorAttributes.SupervisionStrategy>(null);
                return attr != null ? attr.Decider : Deciders.StoppingDecider;
            });
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="function">TBD</param>
        /// <exception cref="ArgumentOutOfRangeException">TBD</exception>
        /// <returns>TBD</returns>
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="ex">TBD</param>
        protected virtual void OnRestart(Exception ex) => OnResume(ex);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="ex">TBD</param>
        protected virtual void OnResume(Exception ex)
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="ex">TBD</param>
        protected virtual void OnStop(Exception ex) => FailStage(ex);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    [InternalApi]
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="func">TBD</param>
        public Collect(Func<TIn, TOut> func)
        {
            _func = func;
            Shape = new FlowShape<TIn, TOut>(In, Out);
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.Collect;

        /// <summary>
        /// TBD
        /// </summary>
        public Inlet<TIn> In { get; } = new Inlet<TIn>("Collect.in");

        /// <summary>
        /// TBD
        /// </summary>
        public Outlet<TOut> Out { get; } = new Outlet<TOut>("Collect.out");

        /// <summary>
        /// TBD
        /// </summary>
        public override FlowShape<TIn, TOut> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            => new Logic(this, inheritedAttributes);

        /// <summary>
        /// Returns a <see cref="string" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="string" /> that represents this instance.
        /// </returns>
        public override string ToString() => "Collect";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    [InternalApi]
    public sealed class Recover<T> : SimpleLinearGraphStage<T>
    {
        #region Logic 

        private sealed class Logic : InAndOutGraphStageLogic
        {
            private readonly Recover<T> _stage;
            private Option<T> _recovered = Option<T>.None;

            public Logic(Recover<T> stage) : base(stage.Shape)
            {
                _stage = stage;

                SetHandler(stage.Inlet, stage.Outlet, this);
            }

            public override void OnPush() => Push(_stage.Outlet, Grab(_stage.Inlet));

            public override void OnUpstreamFailure(Exception ex)
            {
                var result = _stage._recovery(ex);
                if (result.HasValue)
                {
                    if (IsAvailable(_stage.Outlet))
                    {
                        Push(_stage.Outlet, result.Value);
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
                    Push(_stage.Outlet, _recovered.Value);
                    CompleteStage();
                }
                else
                    Pull(_stage.Inlet);
            }


            public override string ToString() => "RecoverLogic";
        }

        #endregion

        private readonly Func<Exception, Option<T>> _recovery;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="recovery">TBD</param>
        public Recover(Func<Exception, Option<T>> recovery) : base("Recover")
        {
            _recovery = recovery;
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.Recover;
        
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        /// <summary>
        /// Returns a <see cref="string" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="string" /> that represents this instance.
        /// </returns>
        public override string ToString() => "Recover";
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// Maps error with the provided function if it is defined for an error or, otherwise, passes it on unchanged.
    /// 
    /// While similar to <see cref="Recover{T}"/> this stage can be used to transform an error signal to a different one without logging
    /// it as an error in the process. So in that sense it is NOT exactly equivalent to Recover(e => throw e2) since Recover
    /// would log the e2 error.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    [InternalApi]
    public sealed class SelectError<T> : SimpleLinearGraphStage<T>
    {
        #region Logic

        private sealed class Logic : InAndOutGraphStageLogic
        {
            private readonly SelectError<T> _stage;

            public Logic(SelectError<T> stage) : base(stage.Shape)
            {
                _stage = stage;
                SetHandler(stage.Inlet, stage.Outlet, this);
            }

            public override void OnPush() => Push(_stage.Outlet, Grab(_stage.Inlet));

            public override void OnPull() => Pull(_stage.Inlet);

            public override void OnUpstreamFailure(Exception e)
            {
                // scala code uses if (f.isDefinedAt(ex)), 
                // doesn't work here so always call the selector and one can simply return e 
                // if no other exception should be used
                base.OnUpstreamFailure(_stage._selector(e));
            }
        }

        #endregion

        private readonly Func<Exception, Exception> _selector;

        public SelectError(Func<Exception, Exception> selector)
        {
            _selector = selector;
        }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    [InternalApi]
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

                SetHandler(stage.Inlet, stage.Outlet, this);
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="count">TBD</param>
        public Take(long count)
        {
            _count = count;
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.Take;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        /// <summary>
        /// Returns a <see cref="string" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="string" /> that represents this instance.
        /// </returns>
        public override string ToString() => "Take";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    [InternalApi]
    public sealed class Skip<T> : SimpleLinearGraphStage<T>
    {
        #region Logic

        private sealed class Logic : InAndOutGraphStageLogic
        {
            private readonly Skip<T> _stage;
            private long _left;

            public Logic(Skip<T> stage) : base(stage.Shape)
            {
                _stage = stage;
                _left = stage._count;

                SetHandler(stage.Inlet, stage.Outlet, this);
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="count">TBD</param>
        public Skip(long count)
        {
            _count = count;
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.Drop;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        /// <summary>
        /// Returns a <see cref="string" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="string" /> that represents this instance.
        /// </returns>
        public override string ToString() => "Skip";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    [InternalApi]
    public sealed class Scan<TIn, TOut> : GraphStage<FlowShape<TIn, TOut>>
    {
        #region Logic 

        private sealed class Logic : InAndOutGraphStageLogic
        {
            private readonly Scan<TIn, TOut> _stage;
            private readonly Action _rest;
            private TOut _stageAggregate;

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

                SetHandler(stage.In, stage.Out, this);
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="zero">TBD</param>
        /// <param name="aggregate">TBD</param>
        public Scan(TOut zero, Func<TOut, TIn, TOut> aggregate)
        {
            _zero = zero;
            _aggregate = aggregate;

            Shape = new FlowShape<TIn, TOut>(In, Out);
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.Scan;

        /// <summary>
        /// TBD
        /// </summary>
        public Inlet<TIn> In { get; } = new Inlet<TIn>("Scan.in");

        /// <summary>
        /// TBD
        /// </summary>
        public Outlet<TOut> Out { get; } = new Outlet<TOut>("Scan.out");

        /// <summary>
        /// TBD
        /// </summary>
        public override FlowShape<TIn, TOut> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            => new Logic(this, inheritedAttributes);

        /// <summary>
        /// Returns a <see cref="string" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="string" /> that represents this instance.
        /// </returns>
        public override string ToString() => "Scan";
    }


    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    [InternalApi]
    public sealed class ScanAsync<TIn, TOut> : GraphStage<FlowShape<TIn, TOut>>
    {
        #region Logic

        private sealed class Logic : InAndOutGraphStageLogic
        {
            private TOut _current;
            private Task<TOut> _evemtualCurrent;
            private readonly ScanAsync<TIn, TOut> _stage;
            private readonly Decider _decider;
            private readonly Action<Result<TOut>> _callback;

            public Logic(ScanAsync<TIn, TOut> stage, Attributes inheritedAttributes) : base(stage.Shape)
            {
                _stage = stage;
                _current = stage._zero;
                _evemtualCurrent = Task.FromResult(_current);

                var attr = inheritedAttributes.GetAttribute<ActorAttributes.SupervisionStrategy>(null);
                _decider = attr != null ? attr.Decider : Deciders.StoppingDecider;

                _callback = GetAsyncCallback<Result<TOut>>(result =>
                {
                    if (result.IsSuccess && result.Value != null)
                    {
                        _current = result.Value;
                        PushAndPullOrFinish(_current);
                        return;
                    }

                    DoSupervision(result.IsSuccess
                        ? ReactiveStreamsCompliance.ElementMustNotBeNullException
                        : result.Exception);
                });

                SetHandler(_stage.Out, () =>
                {
                    Push(_stage.Out, _current);
                    SetHandler(_stage.In, this);
                    SetHandler(_stage.Out, this);
                });

                SetHandler(_stage.In, () => { }, onUpstreamFinish: () => SetHandler(_stage.Out, onPull: () =>
                {
                    Push(_stage.Out, _current);
                    CompleteStage();
                }));
            }

            private void OnRestart() => _current = _stage._zero;

            private void SafePull()
            {
                if (!HasBeenPulled(_stage.In))
                    TryPull(_stage.In);
            }

            private void PushAndPullOrFinish(TOut update)
            {
                Push(_stage.Out, update);

                if (IsClosed(_stage.In))
                    CompleteStage();
                else if (IsAvailable(_stage.Out))
                    SafePull();
            }

            private void DoSupervision(Exception ex)
            {
                switch (_decider(ex))
                {
                    case Directive.Stop:
                        FailStage(ex);
                        break;
                    case Directive.Resume:
                        SafePull();
                        break;
                    case Directive.Restart:
                        OnRestart();
                        SafePull();
                        break;
                }
            }

            public override void OnPush()
            {
                try
                {
                    _evemtualCurrent = _stage._aggregate(_current, Grab(_stage.In));

                    if (_evemtualCurrent.IsCompleted)
                        _callback(Result.FromTask(_evemtualCurrent));
                    else
                        _evemtualCurrent.ContinueWith(t => _callback(Result.FromTask(t)),
                            TaskContinuationOptions.ExecuteSynchronously);
                }
                catch (Exception ex)
                {
                    switch (_decider(ex))
                    {
                        case Directive.Stop:
                            FailStage(ex);
                            break;
                        case Directive.Restart:
                            OnRestart();
                            break;
                        case Directive.Resume:
                            break;
                    }

                    TryPull(_stage.In);
                }
            }

            public override void OnPull() => SafePull();

            public override void OnUpstreamFinish() { }

            public override string ToString() => $"ScanAsync.Logic(completed={_evemtualCurrent.IsCompleted}";
        }

        #endregion

        private readonly Func<TOut, TIn, Task<TOut>> _aggregate;
        private readonly TOut _zero;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="zero">TBD</param>
        /// <param name="aggregate">TBD</param>
        public ScanAsync(TOut zero, Func<TOut, TIn, Task<TOut>> aggregate)
        {
            _zero = zero;
            _aggregate = aggregate;

            Shape = new FlowShape<TIn, TOut>(In, Out);
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.ScanAsync;

        /// <summary>
        /// TBD
        /// </summary>
        public Inlet<TIn> In { get; } = new Inlet<TIn>("ScanAsync.in");

        /// <summary>
        /// TBD
        /// </summary>
        public Outlet<TOut> Out { get; } = new Outlet<TOut>("ScanAsync.out");

        /// <summary>
        /// TBD
        /// </summary>
        public override FlowShape<TIn, TOut> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            => new Logic(this, inheritedAttributes);

        /// <summary>
        /// Returns a <see cref="string" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="string" /> that represents this instance.
        /// </returns>
        public override string ToString() => "ScanAsync";

    }


    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    [InternalApi]
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

                SetHandler(stage.In, stage.Out, this);
            }

            public override void OnPush()
            {
                try
                {
                    _aggregator = _stage._aggregate(_aggregator, Grab(_stage.In));
                }
                catch (Exception ex)
                {
                    var strategy = _decider(ex);
                    if (strategy == Directive.Stop)
                        FailStage(ex);
                    else if (strategy == Directive.Restart)
                        _aggregator = _stage._zero;
                }
                finally
                {
                    if (!IsClosed(_stage.In))
                        Pull(_stage.In);
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="zero">TBD</param>
        /// <param name="aggregate">TBD</param>
        public Aggregate(TOut zero, Func<TOut, TIn, TOut> aggregate)
        {
            _zero = zero;
            _aggregate = aggregate;

            Shape = new FlowShape<TIn, TOut>(In, Out);
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.Aggregate;

        /// <summary>
        /// TBD
        /// </summary>
        public Inlet<TIn> In { get; } = new Inlet<TIn>("Aggregate.in");

        /// <summary>
        /// TBD
        /// </summary>
        public Outlet<TOut> Out { get; } = new Outlet<TOut>("Aggregate.out");

        /// <summary>
        /// TBD
        /// </summary>
        public override FlowShape<TIn, TOut> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            => new Logic(this, inheritedAttributes);

        /// <summary>
        /// Returns a <see cref="string" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="string" /> that represents this instance.
        /// </returns>
        public override string ToString() => "Aggregate";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    [InternalApi]
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
                            if (supervision == Directive.Restart)
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

                SetHandler(stage.In, stage.Out, this);
            }

            public override void OnPush()
            {
                try
                {
                    _aggregating = _stage._aggregate(_aggregator, Grab(_stage.In));
                    HandleAggregatingValue();
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

            public override void OnUpstreamFinish() => HandleAggregatingValue();

            private void HandleAggregatingValue()
            {
                if (_aggregating.IsCompleted)
                    _taskCallback(Result.FromTask(_aggregating));
                else
                    _aggregating.ContinueWith(t => _taskCallback(Result.FromTask(t)),
                        TaskContinuationOptions.ExecuteSynchronously);
            }

            public override void OnPull()
            {
                if (!HasBeenPulled(_stage.In))
                    TryPull(_stage.In);
            }

            private void OnRestart() => _aggregator = _stage._zero;

            public override string ToString() => $"AggregateAsync.Logic(completed={_aggregating.IsCompleted})";
        }

        #endregion

        private readonly TOut _zero;
        private readonly Func<TOut, TIn, Task<TOut>> _aggregate;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="zero">TBD</param>
        /// <param name="aggregate">TBD</param>
        public AggregateAsync(TOut zero, Func<TOut, TIn, Task<TOut>> aggregate)
        {
            _zero = zero;
            _aggregate = aggregate;

            Shape = new FlowShape<TIn, TOut>(In, Out);
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.AggregateAsync;

        /// <summary>
        /// TBD
        /// </summary>
        public Inlet<TIn> In { get; } = new Inlet<TIn>("AggregateAsync.in");

        /// <summary>
        /// TBD
        /// </summary>
        public Outlet<TOut> Out { get; } = new Outlet<TOut>("AggregateAsync.out");

        /// <summary>
        /// TBD
        /// </summary>
        public override FlowShape<TIn, TOut> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            => new Logic(this, inheritedAttributes);

        /// <summary>
        /// Returns a <see cref="string" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="string" /> that represents this instance.
        /// </returns>
        public override string ToString() => "AggregateAsync";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    [InternalApi]
    public sealed class Intersperse<T> : SimpleLinearGraphStage<T>
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
                    _logic.EmitMultiple(_stage.Outlet, new[] { _stage._start, _logic.Grab(_stage.Inlet) });
                else _logic.Emit(_stage.Outlet, _logic.Grab(_stage.Inlet));
                _logic.SetHandler(_stage.Inlet, new RestInHandler(_stage, _logic));
            }

            public override void OnUpstreamFinish()
            {
                _logic.EmitMultiple(_stage.Outlet, new[] { _stage._start, _stage._end });
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
                => _logic.EmitMultiple(_stage.Outlet, new[] { _stage._inject, _logic.Grab(_stage.Inlet) });

            public override void OnUpstreamFinish()
            {
                if (_stage.InjectStartEnd) _logic.Emit(_stage.Outlet, _stage._end);
                _logic.CompleteStage();
            }
        }

        private sealed class Logic : GraphStageLogic, IOutHandler
        {
            private readonly Intersperse<T> _stage;

            public Logic(Intersperse<T> stage) : base(stage.Shape)
            {
                _stage = stage;
                SetHandler(stage.Inlet, new StartInHandler(stage, this));
                SetHandler(stage.Outlet, this);
            }

            public void OnPull() => Pull(_stage.Inlet);

            public void OnDownstreamFinish() => CompleteStage();
        }

        #endregion

        private readonly T _start;
        private readonly T _inject;
        private readonly T _end;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inject">TBD</param>
        public Intersperse(T inject) : base("Intersperse")
        {
            _inject = inject;
            InjectStartEnd = false;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="start">TBD</param>
        /// <param name="inject">TBD</param>
        /// <param name="end">TBD</param>
        public Intersperse(T start, T inject, T end) : base("Intersperse")
        {
            _start = start;
            _inject = inject;
            _end = end;
            InjectStartEnd = true;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public bool InjectStartEnd { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    [InternalApi]
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

                SetHandler(stage.In, stage.Out, this);
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="count">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified <paramref name="count"/> is less than or equal to zero.
        /// </exception>
        public Grouped(int count)
        {
            if (count <= 0)
                throw new ArgumentException("count must be greater than 0", nameof(count));

            _count = count;

            Shape = new FlowShape<T, IEnumerable<T>>(In, Out);
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.Grouped;

        /// <summary>
        /// TBD
        /// </summary>
        public Inlet<T> In { get; } = new Inlet<T>("Grouped.in");

        /// <summary>
        /// TBD
        /// </summary>
        public Outlet<IEnumerable<T>> Out { get; } = new Outlet<IEnumerable<T>>("Grouped.out");

        /// <summary>
        /// TBD
        /// </summary>
        public override FlowShape<T, IEnumerable<T>> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        /// <summary>
        /// Returns a <see cref="string" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="string" /> that represents this instance.
        /// </returns>
        public override string ToString() => "Grouped";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    [InternalApi]
    public sealed class LimitWeighted<T> : SimpleLinearGraphStage<T>
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

                SetHandler(stage.Inlet, this);
                SetHandler(stage.Outlet, this);
            }

            public void OnPush()
            {
                var element = Grab(_stage.Inlet);
                var result = WithSupervision(() => _stage._costFunc(element));
                if (result.HasValue)
                {
                    _left -= result.Value;
                    if (_left >= 0)
                        Push(_stage.Outlet, element);
                    else
                        FailStage(new StreamLimitReachedException(_stage._max));
                }
            }

            public void OnUpstreamFinish() => CompleteStage();

            public void OnUpstreamFailure(Exception e) => FailStage(e);

            public void OnPull() => Pull(_stage.Inlet);

            public void OnDownstreamFinish() => CompleteStage();

            protected override void OnResume(Exception ex) => TryPull();

            protected override void OnRestart(Exception ex)
            {
                _left = _stage._max;
                TryPull();
            }

            private void TryPull()
            {
                if (!HasBeenPulled(_stage.Inlet))
                    Pull(_stage.Inlet);
            }
        }

        #endregion

        private readonly long _max;
        private readonly Func<T, long> _costFunc;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="max">TBD</param>
        /// <param name="costFunc">TBD</param>
        public LimitWeighted(long max, Func<T, long> costFunc) : base("LimitWeighted")
        {
            _max = max;
            _costFunc = costFunc;
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.LimitWeighted;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            => new Logic(this, inheritedAttributes);

        /// <summary>
        /// Returns a <see cref="string" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="string" /> that represents this instance.
        /// </returns>
        public override string ToString() => "LimitWeighted";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    [InternalApi]
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

                SetHandler(stage.In, stage.Out, this);
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="count">TBD</param>
        /// <param name="step">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when either the specified <paramref name="count"/>
        /// or <paramref name="step"/> is less than or equal to zero.
        /// </exception>
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

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.Sliding;

        /// <summary>
        /// TBD
        /// </summary>
        public Inlet<T> In { get; } = new Inlet<T>("Sliding.in");

        /// <summary>
        /// TBD
        /// </summary>
        public Outlet<IEnumerable<T>> Out { get; } = new Outlet<IEnumerable<T>>("Sliding.out");

        /// <summary>
        /// TBD
        /// </summary>
        public override FlowShape<T, IEnumerable<T>> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        /// <summary>
        /// Returns a <see cref="string" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="string" /> that represents this instance.
        /// </returns>
        public override string ToString() => "Sliding";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    [InternalApi]
    public sealed class Buffer<T> : SimpleLinearGraphStage<T>
    {
        #region Logic

        private sealed class Logic : InAndOutGraphStageLogic
        {
            private readonly Buffer<T> _stage;
            private readonly Action<T> _enqueue;
            private IBuffer<T> _buffer;
            

            public Logic(Buffer<T> stage) : base(stage.Shape)
            {
                _stage = stage;

                var strategy = _stage._overflowStrategy;
                switch (strategy)
                {
                    case OverflowStrategy.DropHead:
                        _enqueue = element =>
                        {
                            if (_buffer.IsFull)
                                _buffer.DropHead();
                            _buffer.Enqueue(element);
                            Pull(_stage.Inlet);
                        };
                        break;
                    case OverflowStrategy.DropTail:
                        _enqueue = element =>
                        {
                            if (_buffer.IsFull)
                                _buffer.DropTail();
                            _buffer.Enqueue(element);
                            Pull(_stage.Inlet);
                        };
                        break;
                    case OverflowStrategy.DropBuffer:
                        _enqueue = element =>
                        {
                            if (_buffer.IsFull)
                                _buffer.Clear();
                            _buffer.Enqueue(element);
                            Pull(_stage.Inlet);
                        };
                        break;
                    case OverflowStrategy.DropNew:
                        _enqueue = element =>
                        {
                            if (!_buffer.IsFull)
                                _buffer.Enqueue(element);

                            Pull(_stage.Inlet);
                        };
                        break;
                    case OverflowStrategy.Backpressure:
                        _enqueue = element =>
                        {
                            _buffer.Enqueue(element);

                            if (!_buffer.IsFull)
                                Pull(_stage.Inlet);
                        };
                        break;
                    case OverflowStrategy.Fail:
                        _enqueue = element =>
                        {
                            if (_buffer.IsFull)
                                FailStage(new BufferOverflowException(
                                    $"Buffer overflow (max capacity was {_stage._count})"));
                            else
                            {
                                _buffer.Enqueue(element);
                                Pull(_stage.Inlet);
                            }
                        };
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                SetHandler(_stage.Outlet, this);
                SetHandler(_stage.Inlet, this);
            }

            public override void PreStart()
            {
                _buffer = Buffer.Create<T>(_stage._count, Materializer);
                Pull(_stage.Inlet);
            }

            public override void OnPush()
            {
                var element = Grab(_stage.Inlet);

                // If out is available, then it has been pulled but no dequeued element has been delivered.
                // It means the buffer at this moment is definitely empty,
                // so we just push the current element to out, then pull.
                if (IsAvailable(_stage.Outlet))
                {
                    Push(_stage.Outlet, element);
                    Pull(_stage.Inlet);
                }
                else
                    _enqueue(element);
            }

            public override void OnPull()
            {
                if (_buffer.NonEmpty)
                    Push(_stage.Outlet, _buffer.Dequeue());
                if (IsClosed(_stage.Inlet))
                {
                    if (_buffer.IsEmpty)
                        CompleteStage();
                }
                else if (!HasBeenPulled(_stage.Inlet))
                    Pull(_stage.Inlet);
            }

            public override void OnUpstreamFinish()
            {
                if (_buffer.IsEmpty)
                    CompleteStage();
            }
        }

        #endregion

        private readonly int _count;
        private readonly OverflowStrategy _overflowStrategy;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="count">TBD</param>
        /// <param name="overflowStrategy">TBD</param>
        /// <exception cref="NotSupportedException">
        /// This exception is thrown when the specified <paramref name="overflowStrategy"/>  has an unknown <see cref="OverflowStrategy"/>.
        /// </exception>
        public Buffer(int count, OverflowStrategy overflowStrategy)
        {
            _count = count;
            _overflowStrategy = overflowStrategy;
        }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    [InternalApi]
    public sealed class OnCompleted<T> : GraphStage<FlowShape<T, NotUsed>>
    {
        #region Logic

        private sealed class Logic : InAndOutGraphStageLogic
        {
            private readonly OnCompleted<T> _stage;
            private bool _completionSignalled;

            public Logic(OnCompleted<T> stage) : base(stage.Shape)
            {
                _stage = stage;
                SetHandler(stage.In, stage.Out, this);
            }

            public override void OnPush() => Pull(_stage.In);

            public override void OnPull() => Pull(_stage.In);

            public override void OnUpstreamFinish()
            {
                _stage._success();
                _completionSignalled = true;
                CompleteStage();
            }

            public override void OnUpstreamFailure(Exception e)
            {
                _stage._failure(e);
                _completionSignalled = true;
                FailStage(e);
            }

            public override void PostStop()
            {
                if(!_completionSignalled)
                    _stage._failure(new AbruptStageTerminationException(this));
            }
        }

        #endregion

        private readonly Action _success;
        private readonly Action<Exception> _failure;

        public OnCompleted(Action success, Action<Exception> failure)
        {
            _success = success;
            _failure = failure;

            Shape = new FlowShape<T, NotUsed>(In, Out);
        }

        public Inlet<T> In { get;  } = new Inlet<T>("OnCompleted.in");

        public Outlet<NotUsed> Out { get; } = new Outlet<NotUsed>("OnCompleted.out");

        public override FlowShape<T, NotUsed> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    [InternalApi]
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

                SetHandler(_shape.Inlet, _shape.Outlet, this);
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="max">TBD</param>
        /// <param name="costFunc">TBD</param>
        /// <param name="seed">TBD</param>
        /// <param name="aggregate">TBD</param>
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

        /// <summary>
        /// TBD
        /// </summary>
        public override FlowShape<TIn, TOut> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            => new Logic(inheritedAttributes, this);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    [InternalApi]
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

                SetHandler(stage.In, stage.Out, this);
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="extrapolate">TBD</param>
        public Expand(Func<TIn, IEnumerator<TOut>> extrapolate)
        {
            _extrapolate = extrapolate;

            Shape = new FlowShape<TIn, TOut>(In, Out);
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes => DefaultAttributes.Expand;

        /// <summary>
        /// TBD
        /// </summary>
        public Inlet<TIn> In { get; } = new Inlet<TIn>("expand.in");

        /// <summary>
        /// TBD
        /// </summary>
        public Outlet<TOut> Out { get; } = new Outlet<TOut>("expand.out");

        /// <summary>
        /// TBD
        /// </summary>
        public override FlowShape<TIn, TOut> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        /// <summary>
        /// Returns a <see cref="string" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="string" /> that represents this instance.
        /// </returns>
        public override string ToString() => "Expand";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    [InternalApi]
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

                SetHandler(stage.In, stage.Out, this);
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
                if (!element.IsSuccess && _decider(element.Exception) == Directive.Stop)
                    FailStage(element.Exception);
                else if (IsAvailable(_stage.Out))
                    PushOne();
            }

            public override string ToString() => $"SelectAsync.Logic(buffer={_buffer})";
        }

        #endregion

        private readonly int _parallelism;
        private readonly Func<TIn, Task<TOut>> _mapFunc;

        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<TIn> In = new Inlet<TIn>("SelectAsync.in");
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<TOut> Out = new Outlet<TOut>("SelectAsync.out");

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="parallelism">TBD</param>
        /// <param name="mapFunc">TBD</param>
        public SelectAsync(int parallelism, Func<TIn, Task<TOut>> mapFunc)
        {
            _parallelism = parallelism;
            _mapFunc = mapFunc;
            Shape = new FlowShape<TIn, TOut>(In, Out);
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = Attributes.CreateName("selectAsync");

        /// <summary>
        /// TBD
        /// </summary>
        public override FlowShape<TIn, TOut> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            => new Logic(inheritedAttributes, this);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    [InternalApi]
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
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<TIn> In = new Inlet<TIn>("SelectAsyncUnordered.in");
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<TOut> Out = new Outlet<TOut>("SelectAsyncUnordered.out");

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="parallelism">TBD</param>
        /// <param name="mapFunc">TBD</param>
        public SelectAsyncUnordered(int parallelism, Func<TIn, Task<TOut>> mapFunc)
        {
            _parallelism = parallelism;
            _mapFunc = mapFunc;
            Shape = new FlowShape<TIn, TOut>(In, Out);
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = Attributes.CreateName("selectAsyncUnordered");

        /// <summary>
        /// TBD
        /// </summary>
        public override FlowShape<TIn, TOut> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            => new Logic(inheritedAttributes, this);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    [InternalApi]
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        /// <param name="extract">TBD</param>
        /// <param name="adapter">TBD</param>
        public Log(string name, Func<T, object> extract, ILoggingAdapter adapter)
        {
            _name = name;
            _extract = extract;
            _adapter = adapter;
        }

        // TODO more optimisations can be done here - prepare logOnPush function etc
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            => new Logic(this, inheritedAttributes);

        /// <summary>
        /// Returns a <see cref="string" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="string" /> that represents this instance.
        /// </returns>
        public override string ToString() => "Log";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal enum TimerKeys
    {
        /// <summary>
        /// TBD
        /// </summary>
        TakeWithin,
        /// <summary>
        /// TBD
        /// </summary>
        DropWithin,
        /// <summary>
        /// TBD
        /// </summary>
        GroupedWithin
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    [InternalApi]
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
            private bool _groupEmitted = true;
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

                // Fix for issue #4514
                // Force check if we have a dangling last element because:
                // OnTimer may close the group just before OnUpstreamFinish is called
                // (race condition), dropping the last element in the stream.
                if (IsAvailable(_stage._in))
                    NextElement(Grab(_stage._in));

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
                // Do not pull if we're finished.
                else if (!_finished)
                {
                    Pull(_stage._in);
                }
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
                _buffer = new List<T>(_stage._count);
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="count">TBD</param>
        /// <param name="timeout">TBD</param>
        public GroupedWithin(int count, TimeSpan timeout)
        {
            _count = count;
            _timeout = timeout;
            Shape = new FlowShape<T, IEnumerable<T>>(_in, _out);
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = Attributes.CreateName("GroupedWithin");

        /// <summary>
        /// TBD
        /// </summary>
        public override FlowShape<T, IEnumerable<T>> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    [InternalApi]
    public sealed class Delay<T> : SimpleLinearGraphStage<T>
    {
        #region internal classes

        private sealed class Logic : TimerGraphStageLogic, IInHandler, IOutHandler
        {
            private const string TimerName = "DelayedTimer";
            private readonly Delay<T> _stage;
            private IBuffer<(long, T)> _buffer; // buffer has pairs timestamp with upstream element
            private readonly int _size;
            private readonly Action _onPushWhenBufferFull;

            public Logic(Attributes inheritedAttributes, Delay<T> stage) : base(stage.Shape)
            {
                var inputBuffer = inheritedAttributes.GetAttribute<Attributes.InputBuffer>(null);
                if (inputBuffer == null)
                    throw new IllegalStateException($"Couldn't find InputBuffer Attribute for {this}");

                _stage = stage;
                _size = inputBuffer.Max;
                _onPushWhenBufferFull = OnPushStrategy(_stage._strategy);

                SetHandler(_stage.Inlet, this);
                SetHandler(_stage.Outlet, this);
            }

            public void OnPush()
            {
                if (_buffer.IsFull)
                    _onPushWhenBufferFull();
                else
                {
                    GrabAndPull();
                    if (!IsTimerActive(TimerName))
                        ScheduleOnce(TimerName, _stage._delay);
                }
            }

            public void OnUpstreamFinish() => CompleteIfReady();

            public void OnUpstreamFailure(Exception e) => FailStage(e);

            public void OnPull()
            {
                if (!IsTimerActive(TimerName) && !_buffer.IsEmpty && NextElementWaitTime < 0)
                    Push(_stage.Outlet, _buffer.Dequeue().Item2);

                if (!IsClosed(_stage.Inlet) && !HasBeenPulled(_stage.Inlet) && PullCondition)
                    Pull(_stage.Inlet);

                CompleteIfReady();
            }

            public void OnDownstreamFinish() => CompleteStage();

            private long NextElementWaitTime => (long)_stage._delay.TotalMilliseconds - (DateTime.UtcNow.Ticks - _buffer.Peek().Item1) * 1000 * 10;

            public override void PreStart() => _buffer = Buffer.Create<(long, T)>(_size, Materializer);

            private void CompleteIfReady()
            {
                if (IsClosed(_stage.Inlet) && _buffer.IsEmpty)
                    CompleteStage();
            }

            protected internal override void OnTimer(object timerKey)
            {
                if (IsAvailable(_stage.Outlet))
                    Push(_stage.Outlet, _buffer.Dequeue().Item2);

                if (!_buffer.IsEmpty)
                {
                    var waitTime = NextElementWaitTime;
                    if (waitTime > 10)
                        ScheduleOnce(TimerName, new TimeSpan(waitTime));
                }

                CompleteIfReady();
            }

            private bool PullCondition =>
                _stage._strategy != DelayOverflowStrategy.Backpressure || _buffer.Used < _size;

            private void GrabAndPull()
            {
                _buffer.Enqueue((DateTime.UtcNow.Ticks, Grab(_stage.Inlet)));
                if (PullCondition)
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="delay">TBD</param>
        /// <param name="strategy">TBD</param>
        public Delay(TimeSpan delay, DelayOverflowStrategy strategy)
        {
            _delay = delay;
            _strategy = strategy;
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.Delay;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(inheritedAttributes, this);

        /// <summary>
        /// Returns a <see cref="string" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="string" /> that represents this instance.
        /// </returns>
        public override string ToString() => "Delay";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    [InternalApi]
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="timeout">TBD</param>
        public TakeWithin(TimeSpan timeout)
        {
            _timeout = timeout;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    [InternalApi]
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="timeout">TBD</param>
        public SkipWithin(TimeSpan timeout)
        {
            _timeout = timeout;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    [InternalApi]
    public sealed class Sum<T> : SimpleLinearGraphStage<T>
    {
        #region internal classes

        private sealed class Logic : InAndOutGraphStageLogic
        {
            private readonly Sum<T> _stage;
            private readonly Decider _decider;
            private T _aggregator;

            public Logic(Sum<T> stage, Attributes inheritedAttributes) : base(stage.Shape)
            {
                _stage = stage;
                
                var attr = inheritedAttributes.GetAttribute<ActorAttributes.SupervisionStrategy>(null);
                _decider = attr != null ? attr.Decider : Deciders.StoppingDecider;

                SetInitialInHandler();
                SetHandler(stage.Outlet, this);
            }

            private void SetInitialInHandler()
            {
                SetHandler(_stage.Inlet, onPush: () =>
                {
                    _aggregator = Grab(_stage.Inlet);
                    Pull(_stage.Inlet);
                    SetHandler(_stage.Inlet, this);
                }, onUpstreamFinish: () => FailStage(new NoSuchElementException("Sum over empty stream")));
            }

            public override void OnPush()
            {
                try
                {
                    _aggregator = _stage._reduce(_aggregator, Grab(_stage.Inlet));
                }
                catch (Exception ex)
                {
                    var strategy = _decider(ex);
                    if (strategy == Directive.Stop)
                        FailStage(ex);
                    else if (strategy == Directive.Restart)
                    {
                        _aggregator = default(T);
                        SetInitialInHandler();
                    }
                }
                finally
                {
                    if (!IsClosed(_stage.Inlet))
                        Pull(_stage.Inlet);
                }
            }

            public override void OnPull() => Pull(_stage.Inlet);

            public override void OnUpstreamFinish()
            {
                Push(_stage.Outlet, _aggregator);
                CompleteStage();
            }

            public override string ToString() => $"Sum.Logic(aggregator={_aggregator}";
        }

        #endregion

        private readonly Func<T, T, T> _reduce;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="reduce">TBD</param>
        public Sum(Func<T, T, T> reduce)
        {
            _reduce = reduce;
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.Sum;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this, inheritedAttributes);

        /// <summary>
        /// Returns a <see cref="string" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="string" /> that represents this instance.
        /// </returns>
        public override string ToString() => "Sum";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="TOut">TBD</typeparam>
    /// <typeparam name="TMat">TBD</typeparam>
    [InternalApi]
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

                void PushOut()
                {
                    Push(_stage.Outlet, sinkIn.Grab());
                    if (!sinkIn.IsClosed)
                        sinkIn.Pull();
                    else
                        CompleteStage();
                }

                var outHandler = new LambdaOutHandler(onPull: () =>
                {
                    if (sinkIn.IsAvailable)
                        PushOut();
                }, onDownstreamFinish: () => sinkIn.Cancel());

                Source.FromGraph(source).RunWith(sinkIn.Sink, Interpreter.SubFusingMaterializer);
                SetHandler(_stage.Outlet, outHandler);
                sinkIn.Pull();
            }
        }

        #endregion

        private readonly Func<Exception, IGraph<SourceShape<TOut>, TMat>> _partialFunction;
        private readonly int _maximumRetries;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="partialFunction">TBD</param>
        /// <param name="maximumRetries">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified <paramref name="maximumRetries"/> is less than zero or not equal to -1.
        /// </exception>
        public RecoverWith(Func<Exception, IGraph<SourceShape<TOut>, TMat>> partialFunction, int maximumRetries)
        {
            if (maximumRetries < -1)
                throw new ArgumentException("number of retries must be non-negative or equal to -1", nameof(maximumRetries));

            _partialFunction = partialFunction;
            _maximumRetries = maximumRetries;
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.RecoverWith;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        /// <summary>
        /// Returns a <see cref="string" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="string" /> that represents this instance.
        /// </returns>
        public override string ToString() => "RecoverWith";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    [InternalApi]
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

                SetHandler(stage._in, stage._out, this);
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="concatFactory">TBD</param>
        public StatefulSelectMany(Func<Func<TIn, IEnumerable<TOut>>> concatFactory)
        {
            _concatFactory = concatFactory;

            Shape = new FlowShape<TIn, TOut>(_in, _out);
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.StatefulSelectMany;

        /// <summary>
        /// TBD
        /// </summary>
        public override FlowShape<TIn, TOut> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this, inheritedAttributes);

        /// <summary>
        /// Returns a <see cref="string" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="string" /> that represents this instance.
        /// </returns>
        public override string ToString() => "StatefulSelectMany";
    }
}
