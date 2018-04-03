//-----------------------------------------------------------------------
// <copyright file="Sample.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Streams.Stage;
using Akka.Util;

namespace Akka.Streams.Dsl
{
    /// <summary>
    /// Supports sampling on stream
    /// </summary>
    /// <typeparam name="T">input and output type</typeparam>
    public class Sample<T> : GraphStage<FlowShape<T, T>>
    {
        #region Logic

        private sealed class Logic : GraphStageLogic
        {
            private readonly Sample<T> _sample;
            private int _counter;

            public Logic(Sample<T> sample) : base(sample.Shape)
            {
                _sample = sample;
                var step = GetNextStep();

                SetHandler(sample.Shape.Inlet, onPush: () =>
                {
                    _counter += 1;
                    if (_counter >= step)
                    {
                        _counter = 0;
                        step = GetNextStep();
                        Push(sample.Shape.Outlet, Grab(sample.Shape.Inlet));
                    }
                    else
                    {
                        Pull(sample.Shape.Inlet);
                    }
                });

                SetHandler(sample.Shape.Outlet, onPull: () =>
                {
                    Pull(sample.Shape.Inlet);
                });
            }

            private int GetNextStep()
            {
                var nextStep = _sample._next();

                if (nextStep <= 0)
                    throw new ArgumentException($"Sampling step should be a positive value: {nextStep}");

                return nextStep;
            }
        }

        #endregion

        /// <summary>
        /// Randomly sampling on a stream
        /// </summary>
        /// <param name="maxStep">must > 0, default 1000, the randomly step will be between 1 (inclusive) and <paramref name="maxStep"/> (inclusive)</param>
        public static Sample<T> Random(int maxStep = 1000)
        {
            if (maxStep <= 0)
                throw new ArgumentException($"Max step for a random sampling must > 0", nameof(maxStep));

            return new Sample<T>(() => ThreadLocalRandom.Current.Next(maxStep) + 1);
        }

        private readonly Func<int> _next;

        /// <summary>
        /// Returns every <paramref name="n"/>th elements
        /// </summary>
        /// <param name="n"><paramref name="n"/>th element. <paramref name="n"/> must > 0</param>
        public Sample(int n) : this(() => n)
        {
        }

        /// <param name="next">a lambda returns next sample position</param>
        public Sample(Func<int> next)
        {
            _next = next;

            In = new Inlet<T>("Sample-in");
            Out = new Outlet<T>("Sample-out");
            Shape = new FlowShape<T, T>(In, Out);
        }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        public override FlowShape<T, T> Shape { get; }

        public Inlet<T> In = new Inlet<T>("Sample-in");
        public Outlet<T> Out = new Outlet<T>("Sample-out");
    }
}
