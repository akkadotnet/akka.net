//-----------------------------------------------------------------------
// <copyright file="DelayFlow.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Annotations;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Stage;

namespace Akka.Streams.Dsl
{
    /// <summary>
    /// Allows to manage delay and can be stateful to compute delay for any sequence of elements,
    /// all elements go through <see cref="NextDelay(T)"/> updating state and returning delay for each element
    /// </summary>
    /// <typeparam name="T">type of element</typeparam>
    public interface IDelayStrategy<T>
    {
        /// <summary>
        /// Returns delay for ongoing <paramref name="element"/>, <code>TimeSpan.Zero</code> means passing without delay
        /// </summary>
        /// <param name="element">element</param>
        /// <returns></returns>
        TimeSpan NextDelay(T element);
    }

    /// <summary>
    /// Fixed delay strategy, always returns constant delay for any element.
    /// </summary>
    /// <typeparam name="T">type of element</typeparam>
    public class FixedDelay<T> : IDelayStrategy<T>
    {
        private readonly TimeSpan _delay;

        /// <param name="delay">value of the delay</param>
        public FixedDelay(TimeSpan delay) => _delay = delay;

        /// <inheritdoc/>
        public TimeSpan NextDelay(T element) => _delay;
    }

    /// <summary>
    /// Strategy with linear increasing delay.
    /// </summary>
    /// <typeparam name="T">type of element</typeparam>
    public class LinearIncreasingDelay<T> : IDelayStrategy<T>
    {
        private readonly TimeSpan _increaseStep;
        private readonly Func<T, bool> _needsIncrease;
        private readonly TimeSpan _initialDelay;
        private readonly TimeSpan _maxDelay;

        private TimeSpan _delay;

        /// <summary>
        /// Creates strategy that starts with <paramref name="initialDelay"/> for each element,
        /// increases by <paramref name="increaseStep"/> every time when <paramref name="needsIncrease"/> returns <code>true</code> up to <paramref name="maxDelay"/>,
        /// when <paramref name="needsIncrease"/> returns <code>false</code> it resets to <paramref name="initialDelay"/>.
        /// </summary>
        /// <param name="increaseStep">step by which delay is increased</param>
        /// <param name="needsIncrease">if <code>true</code> delay increases, if <code>false</code> delay resets to <paramref name="initialDelay"/></param>
        /// <param name="initialDelay">initial delay for each of elements</param>
        /// <param name="maxDelay">limits maximum delay</param>
        [ApiMayChange]
        public LinearIncreasingDelay(TimeSpan increaseStep, Func<T, bool> needsIncrease, TimeSpan initialDelay, TimeSpan maxDelay)
        {
            if (increaseStep <= TimeSpan.Zero)
                throw new ArgumentException("Increase step must be positive", nameof(increaseStep));

            if (maxDelay <= initialDelay)
                throw new ArgumentException("Max delay must be bigger than initial delay", nameof(maxDelay));

            _increaseStep = increaseStep;
            _needsIncrease = needsIncrease;
            _initialDelay = initialDelay;
            _maxDelay = maxDelay;

            _delay = _initialDelay;
        }

        /// <inheritdoc/>
        public TimeSpan NextDelay(T element)
        {
            if (_needsIncrease(element))
            {
                var next = _delay + _increaseStep;
                if (next < _maxDelay)
                    _delay = next;
                else
                    _delay = _maxDelay;
            }
            else
                _delay = _initialDelay;

            return _delay;
        }
    }

    /// <summary>
    /// Flow stage for universal delay management, allows to manage delay through <see cref="IDelayStrategy{T}"/>.
    /// </summary>
    /// <typeparam name="T">type of element</typeparam>
    public class DelayFlow<T> : SimpleLinearGraphStage<T>
    {
        #region Logic

        private sealed class Logic : TimerGraphStageLogic, IInHandler, IOutHandler
        {
            private readonly DelayFlow<T> _delayFlow;
            private readonly object _delayTimerKey = new object();
            private readonly IDelayStrategy<T> _strategy;
            private T _delayedElement;

            public Logic(DelayFlow<T> delayFlow) : base(delayFlow.Shape)
            {
                _delayFlow = delayFlow;
                _strategy = delayFlow._strategySupplier();

                SetHandler(delayFlow.Inlet, this);
                SetHandler(delayFlow.Outlet, this);
            }

            public void OnPush()
            {
                var element = Grab(_delayFlow.Inlet);
                var delay = _strategy.NextDelay(element);
                if (delay == TimeSpan.Zero)
                    Push(_delayFlow.Outlet, element);
                else
                {
                    _delayedElement = element;
                    ScheduleOnce(_delayTimerKey, delay);
                }
            }

            public void OnUpstreamFinish()
            {
                if (!IsTimerActive(_delayTimerKey))
                    CompleteStage();
            }

            public void OnUpstreamFailure(Exception e) => FailStage(e);

            public void OnPull()
            {
                Pull(_delayFlow.Inlet);
            }

            public void OnDownstreamFinish() => CompleteStage();

            protected internal override void OnTimer(object timerKey)
            {
                Push(_delayFlow.Outlet, _delayedElement);
                if (IsClosed(_delayFlow.Inlet))
                    CompleteStage();
            }
        }

        #endregion

        private readonly Func<IDelayStrategy<T>> _strategySupplier;

        /// <summary>
        /// Flow stage that determines delay for each ongoing element invoking <code>TimeSpan DelayStrategy.NextDelay(T element)</code>.
        /// Implementing <see cref="IDelayStrategy{T}"/> with your own gives you flexible ability to manage delay value depending on coming elements.
        /// It is important notice that <see cref="IDelayStrategy{T}"/> can be stateful.
        /// There are also some predefined strategies: <see cref="FixedDelay{T}"/> and <see cref="LinearIncreasingDelay{T}"/>
        /// </summary>
        /// <param name="strategySupplier">creates new <see cref="IDelayStrategy{T}"/> object for each materialization</param>
        public DelayFlow(Func<IDelayStrategy<T>> strategySupplier)
        {
            _strategySupplier = strategySupplier;
        }

        /// <summary>
        /// Flow stage with fixed delay for each element.
        /// </summary>
        /// <param name="fixedDelay">value of the delay</param>
        public DelayFlow(TimeSpan fixedDelay)
        {
            var delayStrategy = new FixedDelay<T>(fixedDelay);
            _strategySupplier = () => delayStrategy;
        }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
    }
}
