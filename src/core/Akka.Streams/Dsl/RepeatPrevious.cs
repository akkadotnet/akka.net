//-----------------------------------------------------------------------
// <copyright file="RepeatPrevious.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Streams.Stage;
using Akka.Util;

namespace Akka.Streams.Dsl
{
    /// <summary>
    /// Delegate used to customize replacement functions.
    ///
    /// This is used for things like calling <see cref="IDisposable.Dispose"/> on the <see cref="previousValue"/>
    /// when a new value is emitted.
    /// </summary>
    /// <typeparam name="T">The type of element handled by the <see cref="RepeatPrevious{T}"/></typeparam>
    public delegate void SwapPrevious<T>(T previousValue, T newValue);

    /// <summary>
    /// Repeats the previous element from upstream until it's replaced by a new value.
    ///
    /// This is designed to allow fan-in stages where output from one of the sources is intermittent / infrequent
    /// and users just want the previous value to be reused.
    /// </summary>
    /// <typeparam name="T">The output type.</typeparam>
    public sealed class RepeatPrevious<T> : GraphStage<FlowShape<T, T>>
    {
        private readonly Inlet<T> _in = new Inlet<T>("RepeatPrevious.in");
        private readonly Outlet<T> _out = new Outlet<T>("RepeatPrevious.out");

        public override FlowShape<T, T> Shape => new FlowShape<T, T>(_in, _out);
        private readonly SwapPrevious<T> _swapPrevious;

        /// <summary>
        /// Do nothing by default
        /// </summary>
        private static readonly SwapPrevious<T> DefaultSwap = (value, newValue) => { };

        public RepeatPrevious() : this(DefaultSwap)
        {
        }

        public RepeatPrevious(SwapPrevious<T> swapPrevious)
        {
            _swapPrevious = swapPrevious;
        }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) =>
            new Logic(this, _swapPrevious);

        private sealed class Logic : InAndOutGraphStageLogic
        {
            private readonly RepeatPrevious<T> _stage;
            private Option<T> _last;
            private readonly SwapPrevious<T> _swapPrevious;
            private bool _pulled = false;

            public Logic(RepeatPrevious<T> stage, SwapPrevious<T> swapPrevious) : base(stage.Shape)
            {
                _stage = stage;
                _swapPrevious = swapPrevious;

                SetHandler(_stage._in, this);
                SetHandler(_stage._out, this);
            }

            public override void OnDownstreamFinish()
            {
                base.OnDownstreamFinish();
            }

            public override void OnPush()
            {
                var next = Grab(_stage._in);
                if (_last.HasValue)
                    _swapPrevious(_last.Value, next);
                _last = next;

                if (IsAvailable(_stage._out))
                {
                    Push(_stage._out, _last.Value);
                }
            }
            
            public override void OnPull()
            {
                if (_last.HasValue)
                {
                    if (!HasBeenPulled(_stage._in))
                    {
                        Pull(_stage._in);
                    }

                    Push(_stage._out, _last.Value);
                }
                else
                {
                    Pull(_stage._in);
                }
            }
        }

        public override string ToString()
        {
            return "RepeatPrevious";
        }
    }
}