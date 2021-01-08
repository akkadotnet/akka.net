//-----------------------------------------------------------------------
// <copyright file="Sample.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
            private int _step;

            public Logic(Sample<T> sample) : base(sample.Shape)
            {
                _sample = sample;
                _step = GetNextStep();

                SetHandler(sample.In, onPush: () =>
                {
                    _counter += 1;
                    if (_counter >= _step)
                    {
                        _counter = 0;
                        _step = GetNextStep();
                        Push(sample.Out, Grab(sample.In));
                    }
                    else
                        Pull(sample.In);
                });

                SetHandler(sample.Out, onPull: () =>
                {
                    Pull(sample.In);
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
        /// Returns every <paramref name="nth"/> element
        /// </summary>
        /// <param name="nth"><paramref name="nth"/> element. <paramref name="nth"/> must > 0</param>
        public Sample(int nth) : this(() => nth)
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

        public Inlet<T> In { get; }
        public Outlet<T> Out { get; }
    }
}
