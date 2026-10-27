//-----------------------------------------------------------------------
// <copyright file="Unfold.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Annotations;
using Akka.Streams.Stage;
using Akka.Streams.Util;
using Akka.Util;

namespace Akka.Streams.Implementation
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="TState">The state passed from one unfolding step to the next.</typeparam>
    /// <typeparam name="TElement">The type of elements emitted by the source.</typeparam>
    [InternalApi]
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
                if (!t.HasValue)
                    Complete(_stage.Out);
                else
                {
                    Push(_stage.Out, t.Value.Item2);
                    _state = t.Value.Item1;
                }
            }
        }
        #endregion

        /// <summary>
        /// Gets the initial state for this source.
        /// </summary>
        public readonly TState State;
        /// <summary>
        /// Computes the next state and element, or returns no value to complete the source.
        /// </summary>
        public readonly Func<TState, Option<(TState, TElement)>> UnfoldFunc;
        /// <summary>
        /// Gets the source outlet.
        /// </summary>
        public readonly Outlet<TElement> Out = new("Unfold.out");

        /// <summary>
        /// Creates a source that computes one state transition for each downstream pull.
        /// </summary>
        /// <param name="state">The initial state supplied to <paramref name="unfoldFunc"/>.</param>
        /// <param name="unfoldFunc">Returns the next state and element, or no value to finish.</param>
        public Unfold(TState state, Func<TState, Option<(TState, TElement)>> unfoldFunc)
        {
            State = state;
            UnfoldFunc = unfoldFunc;
            Shape = new SourceShape<TElement>(Out);
        }

        /// <summary>
        /// Gets the outlet-only source shape.
        /// </summary>
        public override SourceShape<TElement> Shape { get; }

        /// <summary>
        /// Creates the logic that evaluates the unfolding function on demand.
        /// </summary>
        /// <param name="inheritedAttributes">Attributes applied to the stage.</param>
        /// <returns>Logic that evaluates one step whenever downstream pulls.</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="TState">The state passed from one unfolding step to the next.</typeparam>
    /// <typeparam name="TElement">The type of elements emitted by the source.</typeparam>
    [InternalApi]
    public class UnfoldAsync<TState, TElement> : GraphStage<SourceShape<TElement>>
    {
        #region stage logic
        private sealed class Logic : OutGraphStageLogic
        {
            private readonly UnfoldAsync<TState, TElement> _stage;
            private TState _state;
            private Action<Result<Option<(TState, TElement)>>> _asyncHandler;

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
                var ac = GetAsyncCallback<Result<Option<(TState, TElement)>>>(result =>
                {
                    if (!result.IsSuccess)
                        Fail(_stage.Out, result.Exception);
                    else
                    {
                        var option = result.Value;
                        if (!option.HasValue)
                            Complete(_stage.Out);
                        else
                        {
                            Push(_stage.Out, option.Value.Item2);
                            _state = option.Value.Item1;
                        }
                    }
                });
                _asyncHandler = ac;
            }
        }
        #endregion

        /// <summary>
        /// Gets the initial state for this source.
        /// </summary>
        public readonly TState State;
        /// <summary>
        /// Asynchronously computes the next state and element, or returns no value to complete the source.
        /// </summary>
        public readonly Func<TState, Task<Option<(TState, TElement)>>> UnfoldFunc;
        /// <summary>
        /// Gets the source outlet.
        /// </summary>
        public readonly Outlet<TElement> Out = new("UnfoldAsync.out");

        /// <summary>
        /// Creates a source that asynchronously computes one state transition for each downstream pull.
        /// </summary>
        /// <param name="state">The initial state supplied to <paramref name="unfoldFunc"/>.</param>
        /// <param name="unfoldFunc">Returns a task with the next state and element, or no value to finish.</param>
        public UnfoldAsync(TState state, Func<TState, Task<Option<(TState, TElement)>>> unfoldFunc)
        {
            State = state;
            UnfoldFunc = unfoldFunc;
            Shape = new SourceShape<TElement>(Out);
        }

        /// <summary>
        /// Gets the outlet-only source shape.
        /// </summary>
        public override SourceShape<TElement> Shape { get; }

        /// <summary>
        /// Creates the logic that evaluates the asynchronous unfolding function on demand.
        /// </summary>
        /// <param name="inheritedAttributes">Attributes applied to the stage.</param>
        /// <returns>Logic that evaluates and awaits one step whenever downstream pulls.</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
    }
    
    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="TState">The state passed from one unfolding step to the next.</typeparam>
    /// <typeparam name="TElement">The type of elements emitted by the source.</typeparam>
    [InternalApi]
    public class UnfoldInfinite<TState, TElement> : GraphStage<SourceShape<TElement>>
    {
        #region internal classes
        private sealed class Logic : OutGraphStageLogic
        {
            private readonly UnfoldInfinite<TState, TElement> _stage;
            private TState _state;

            public Logic(UnfoldInfinite<TState, TElement> stage) : base(stage.Shape)
            {
                _stage = stage;
                _state = _stage.State;

                SetHandler(_stage.Out, this);
            }

            public override void OnPull()
            {
                var t = _stage.UnfoldFunc(_state);
                
                Push(_stage.Out, t.Item2);
                _state = t.Item1;
            }
        }
        #endregion

        /// <summary>
        /// Gets the initial state for this source.
        /// </summary>
        public readonly TState State;
        /// <summary>
        /// Computes the next state and element for each downstream pull.
        /// </summary>
        public readonly Func<TState, (TState, TElement)> UnfoldFunc;
        /// <summary>
        /// Gets the source outlet.
        /// </summary>
        public readonly Outlet<TElement> Out = new("UnfoldInfinite.out");

        /// <summary>
        /// Creates a source that computes a state transition for every downstream pull and has no completion result.
        /// </summary>
        /// <param name="state">The initial state supplied to <paramref name="unfoldFunc"/>.</param>
        /// <param name="unfoldFunc">Returns the next state and element.</param>
        public UnfoldInfinite(TState state, Func<TState, (TState, TElement)> unfoldFunc)
        {
            State = state;
            UnfoldFunc = unfoldFunc;
            Shape = new SourceShape<TElement>(Out);
        }

        /// <summary>
        /// Gets the outlet-only source shape.
        /// </summary>
        public override SourceShape<TElement> Shape { get; }

        /// <summary>
        /// Creates the logic that evaluates the unfolding function on demand.
        /// </summary>
        /// <param name="inheritedAttributes">Attributes applied to the stage.</param>
        /// <returns>Logic that evaluates one step whenever downstream pulls.</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
    }
}
