//-----------------------------------------------------------------------
// <copyright file="RepeatPrevious.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Streams.Stage;

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
        
        public RepeatPrevious() : this(DefaultSwap){ }

        public RepeatPrevious(SwapPrevious<T> swapPrevious)
        {
            _swapPrevious = swapPrevious;
        }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this, _swapPrevious);

        private sealed class Logic : GraphStageLogic
        {
            private readonly RepeatPrevious<T> _stage;
            private T _last;
            private readonly SwapPrevious<T> _swapPrevious;

            public Logic(RepeatPrevious<T> stage, SwapPrevious<T> swapPrevious) : base(stage.Shape)
            {
                _stage = stage;
                _swapPrevious = swapPrevious;
                SetHandler(stage._in, onPush: () =>
                {
                    var next = Grab(stage._in);
                    swapPrevious(_last, next);
                    _last = next;
                    Push(stage._out, _last);
                }, onUpstreamFinish: () => Complete(stage._out));

                SetHandler(stage._out, onPull: () =>
                {
                    if (_last != null)
                        Push(stage._out, _last);
                    else
                        Pull(stage._in);
                });
            }

            public override void PostStop()
            {
                DisposeIfNecessary();
                base.PostStop();
            }

            private void DisposeIfNecessary()
            {
                if (_last is IDisposable disposable)
                    disposable.Dispose();
            }
        }
    }

}