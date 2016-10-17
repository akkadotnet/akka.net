//-----------------------------------------------------------------------
// <copyright file="Unfold.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Streams.Stage;
using Akka.Util;

namespace Akka.Streams.Implementation
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    public class Unfold<TState, TElement> : GraphStage<SourceShape<TElement>>
    {
        #region internal classes
        private sealed class Logic : OutGraphStageLogic
        {
            private readonly Unfold<TState, TElement> _stage;
            private TState _state;

            public Logic(Unfold<TState, TElement> stage) : base(stage.Shape)
            {
                _stage = stage;
                _state = _stage.State;

                SetHandler(_stage.Out, this);
            }

            public override void OnPull()
            {
                var t = _stage.UnfoldFunc(_state);
                if (t == null)
                    Complete(_stage.Out);
                else
                {
                    Push(_stage.Out, t.Item2);
                    _state = t.Item1;
                }
            }
        }
        #endregion

        public readonly TState State;
        public readonly Func<TState, Tuple<TState, TElement>> UnfoldFunc;
        public readonly Outlet<TElement> Out = new Outlet<TElement>("Unfold.out");

        public Unfold(TState state, Func<TState, Tuple<TState, TElement>> unfoldFunc)
        {
            State = state;
            UnfoldFunc = unfoldFunc;
            Shape = new SourceShape<TElement>(Out);
        }

        public override SourceShape<TElement> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    public class UnfoldAsync<TState, TElement> : GraphStage<SourceShape<TElement>>
    {
        #region stage logic
        private sealed class Logic : OutGraphStageLogic
        {
            private readonly UnfoldAsync<TState, TElement> _stage;
            private TState _state;
            private Action<Result<Tuple<TState, TElement>>> _asyncHandler;

            public Logic(UnfoldAsync<TState, TElement> stage) : base(stage.Shape)
            {
                _stage = stage;
                _state = _stage.State;

                SetHandler(_stage.Out, this);
            }

            public override void OnPull()
            {
                _stage.UnfoldFunc(_state)
                    .ContinueWith(task => _asyncHandler(Result.FromTask(task)),
                        TaskContinuationOptions.AttachedToParent);
            }

            public override void PreStart()
            {
                var ac = GetAsyncCallback<Result<Tuple<TState, TElement>>>(result =>
                {
                    if (!result.IsSuccess)
                        Fail(_stage.Out, result.Exception);
                    else if (result.Value == null)
                        Complete(_stage.Out);
                    else
                    {
                        Push(_stage.Out, result.Value.Item2);
                        _state = result.Value.Item1;
                    }
                });
                _asyncHandler = ac;
            }
        }
        #endregion

        public readonly TState State;
        public readonly Func<TState, Task<Tuple<TState, TElement>>> UnfoldFunc;
        public readonly Outlet<TElement> Out = new Outlet<TElement>("UnfoldAsync.out");

        public UnfoldAsync(TState state, Func<TState, Task<Tuple<TState, TElement>>> unfoldFunc)
        {
            State = state;
            UnfoldFunc = unfoldFunc;
            Shape = new SourceShape<TElement>(Out);
        }

        public override SourceShape<TElement> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
    }
}