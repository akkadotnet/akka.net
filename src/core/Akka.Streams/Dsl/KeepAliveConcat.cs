//-----------------------------------------------------------------------
// <copyright file="KeepAliveConcat.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Streams.Stage;

namespace Akka.Streams.Dsl
{
    /// <summary>
    /// Sends elements from buffer if upstream does not emit for a configured amount of time. In other words, this
    /// stage attempts to maintains a base rate of emitted elements towards the downstream using elements from upstream.
    /// <para/>
    /// If upstream emits new elements until the accumulated elements in the buffer exceed the specified minimum size
    /// used as the keep alive elements, then the base rate is no longer maintained until we reach another period without
    /// elements form upstream.
    /// <para/>
    /// The keep alive period is the keep alive failover size times the interval.
    /// <para>
    /// Emits when upstream emits an element or if the upstream was idle for the configured period
    /// </para>
    /// <para>
    /// Backpressures when downstream backpressures
    /// </para>
    /// <para>
    /// Completes when upstream completes
    /// </para>
    /// Cancels when downstream cancels
    /// </summary>
    /// <typeparam name="T">type of element</typeparam>
    public class KeepAliveConcat<T> : GraphStage<FlowShape<T, T>>
    {
        private readonly int _keepAliveFailoverSize;
        private readonly TimeSpan _interval;
        private readonly Func<T, IEnumerable<T>> _extrapolate;

        #region Logic

        private sealed class Logic : TimerGraphStageLogic, IInHandler, IOutHandler
        {
            private readonly KeepAliveConcat<T> _keepAliveConcat;
            private readonly Queue<T> _buffer;

            public Logic(KeepAliveConcat<T> keepAliveConcat) : base(keepAliveConcat.Shape)
            {
                _keepAliveConcat = keepAliveConcat;
                _buffer = new Queue<T>(_keepAliveConcat._keepAliveFailoverSize);

                SetHandler(_keepAliveConcat.In, this);
                SetHandler(_keepAliveConcat.Out, this);
            }

            public void OnPush()
            {
                var elem = Grab(_keepAliveConcat.In);
                if (_buffer.Count < _keepAliveConcat._keepAliveFailoverSize)
                {
                    foreach (var t in _keepAliveConcat._extrapolate(elem))
                        _buffer.Enqueue(t);
                }
                else
                    _buffer.Enqueue(elem);


                if (IsAvailable(_keepAliveConcat.Out) && _buffer.Count > 0)
                    Push(_keepAliveConcat.Out, _buffer.Dequeue());
                else
                    Pull(_keepAliveConcat.In);
            }

            public void OnUpstreamFinish()
            {
                if (_buffer.Count == 0)
                    CompleteStage();
            }

            public void OnUpstreamFailure(Exception e) => FailStage(e);

            public void OnPull()
            {
                if (IsClosed(_keepAliveConcat.In))
                {
                    if (_buffer.Count == 0)
                        CompleteStage();
                    else
                        Push(_keepAliveConcat.Out, _buffer.Dequeue());
                }
                else if (_buffer.Count > _keepAliveConcat._keepAliveFailoverSize)
                    Push(_keepAliveConcat.Out, _buffer.Dequeue());
                else if (!HasBeenPulled(_keepAliveConcat.In))
                    Pull(_keepAliveConcat.In);
            }

            public void OnDownstreamFinish() => CompleteStage();

            protected internal override void OnTimer(object timerKey)
            {
                if (IsAvailable(_keepAliveConcat.Out) && _buffer.Count > 0)
                    Push(_keepAliveConcat.Out, _buffer.Dequeue());
            }

            public override void PreStart()
            {
                ScheduleRepeatedly("KeepAliveConcatTimer", _keepAliveConcat._interval);
                Pull(_keepAliveConcat.In);
            }
        }

        #endregion

        public KeepAliveConcat(int keepAliveFailoverSize, TimeSpan interval, Func<T, IEnumerable<T>> extrapolate)
        {
            if (keepAliveFailoverSize <= 0)
                throw new ArgumentException("The buffer keep alive failover size must be greater than 0.", nameof(keepAliveFailoverSize));

            _keepAliveFailoverSize = keepAliveFailoverSize;
            _interval = interval;
            _extrapolate = extrapolate;

            In = new Inlet<T>("KeepAliveConcat.in");
            Out = new Outlet<T>("KeepAliveConcat.out");
            Shape = new FlowShape<T, T>(In, Out);
        }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        public override FlowShape<T, T> Shape { get; }

        public Inlet<T> In { get; }
        public Outlet<T> Out { get; }
    }
}
