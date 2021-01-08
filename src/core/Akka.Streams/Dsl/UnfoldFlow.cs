//-----------------------------------------------------------------------
// <copyright file="UnfoldFlow.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Annotations;
using Akka.Streams.Stage;

namespace Akka.Streams.Dsl
{
    [InternalApi]
    internal abstract class UnfoldFlowGraphStageLogic<TIn, TState, TOut> : GraphStageLogic, IOutHandler
    {
        private readonly TimeSpan _timeout;
        private readonly Outlet<TState> _feedback;
        protected readonly Outlet<TOut> _output;
        protected readonly Inlet<TIn> _nextElem;

        protected TState _pending;
        protected bool _pushedToCycle;

        protected UnfoldFlowGraphStageLogic(FanOutShape<TIn, TState, TOut> shape, TState seed, TimeSpan timeout) : base(shape)
        {
            _timeout = timeout;

            _feedback = shape.Out0;
            _output = shape.Out1;
            _nextElem = shape.In;

            _pending = seed;
            _pushedToCycle = false;

            SetHandler(_feedback, this);

            SetHandler(_output, onPull: () =>
            {
                Pull(_nextElem);
                if (!_pushedToCycle && IsAvailable(_feedback))
                {
                    Push(_feedback, _pending);
                    _pending = default(TState);
                    _pushedToCycle = true;
                }
            });
        }

        public void OnPull()
        {
            if (!_pushedToCycle && IsAvailable(_output))
            {
                Push(_feedback, _pending);
                _pending = default(TState);
                _pushedToCycle = true;
            }
        }

        public void OnDownstreamFinish()
        {
            // Do Nothing until `timeout` to try and intercept completion as downstream,
            // but cancel stream after timeout if inlet is not closed to prevent deadlock.
            Materializer.ScheduleOnce(_timeout, () =>
            {
                var cb = GetAsyncCallback(() =>
                {
                    if (!IsClosed(_nextElem))
                        FailStage(new InvalidOperationException($"unfoldFlow source's inner flow canceled only upstream, while downstream remain available for {_timeout}"));
                });
                cb();
            });
        }
    }

    [InternalApi]
    internal class FanOut2UnfoldingStage<TIn, TState, TOut> : GraphStage<FanOutShape<TIn, TState, TOut>>
    {
        private readonly Func<FanOutShape<TIn, TState, TOut>, UnfoldFlowGraphStageLogic<TIn, TState, TOut>> _generateGraphStageLogic;

        public FanOut2UnfoldingStage(Func<FanOutShape<TIn, TState, TOut>, UnfoldFlowGraphStageLogic<TIn, TState, TOut>> generateGraphStageLogic)
        {
            _generateGraphStageLogic = generateGraphStageLogic;

            Shape = new FanOutShape<TIn, TState, TOut>("unfoldFlow");            
        }

        public override FanOutShape<TIn, TState, TOut> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return _generateGraphStageLogic(Shape);
        }
    }
}
