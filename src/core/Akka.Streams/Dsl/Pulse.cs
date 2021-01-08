//-----------------------------------------------------------------------
// <copyright file="Pulse.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Stage;

namespace Akka.Streams.Dsl
{
    /// <summary>
    /// Pulse stage signals demand only once every "pulse" interval and then back-pressures.
    /// Requested element is emitted downstream if there is demand.
    /// It can be used to implement simple time-window processing
    /// where data is aggregated for predefined amount of time and the computed aggregate is emitted once per this time.
    /// </summary>
    /// <typeparam name="T">type of element</typeparam>
    public class Pulse<T> : SimpleLinearGraphStage<T>
    {
        private readonly TimeSpan _interval;
        private readonly bool _initiallyOpen;

        #region Logic

        private sealed class Logic : TimerGraphStageLogic, IInHandler, IOutHandler
        {
            private readonly Pulse<T> _pulse;
            private bool _pulsing;

            public Logic(Pulse<T> pulse) : base(pulse.Shape)
            {
                _pulse = pulse;

                SetHandler(_pulse.Inlet, this);
                SetHandler(pulse.Outlet, this);
            }

            public void OnPush()
            {
                if (IsAvailable(_pulse.Outlet))
                    Push(_pulse.Outlet, Grab(_pulse.Inlet));
            }

            public void OnUpstreamFinish() => CompleteStage();

            public void OnUpstreamFailure(Exception e) => FailStage(e);

            public void OnPull()
            {
                if (!_pulsing)
                {
                    Pull(_pulse.Inlet);
                    StartPulsing();
                }
            }

            public void OnDownstreamFinish() => CompleteStage();

            protected internal override void OnTimer(object timerKey)
            {
                if (IsAvailable(_pulse.Outlet) && !IsClosed(_pulse.Inlet) && !HasBeenPulled(_pulse.Inlet))
                    Pull(_pulse.Inlet);
            }

            public override void PreStart()
            {
                if (!_pulse._initiallyOpen)
                    StartPulsing();
            }

            private void StartPulsing()
            {
                _pulsing = true;
                ScheduleRepeatedly("PulseTimer", _pulse._interval);
            }
        }

        #endregion

        /// Creates Pulse stage
        /// <param name="interval">"pulse" period</param>
        /// <param name="initiallyOpen">if <code>true</code> - emits the first available element before "pulsing"</param>
        public Pulse(TimeSpan interval, bool initiallyOpen = false)
        {
            _interval = interval;
            _initiallyOpen = initiallyOpen;
        }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
    }
}
