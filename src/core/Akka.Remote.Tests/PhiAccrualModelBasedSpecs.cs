//-----------------------------------------------------------------------
// <copyright file="PhiAccrualModelBasedSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#if FSCHECK
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Util;
using FluentAssertions;
using FsCheck;
using FsCheck.Experimental;
using FsCheck.Xunit;
using Microsoft.FSharp.Core;
using Xunit;

namespace Akka.Remote.Tests
{
    public class PhiAccrualModelBasedSpecs
    {
        [Property(StartSize = 10, MaxTest = 1000)]
        public Property PhiAccrualHistoryShouldBeValid()
        {
            return new PhiAccrualMachine().ToProperty();
        }
    }

    internal class PhiAccrualState
    {
        public HeartbeatHistory History { get; set; }
    }

    internal class PhiAccrualMachine : Machine<PhiAccrualState, PhiAccrualModel>
    {
        public override Gen<Operation<PhiAccrualState, PhiAccrualModel>> Next(PhiAccrualModel obj0)
        {
            return NextInterval.Generator();
        }

        public override Arbitrary<Setup<PhiAccrualState, PhiAccrualModel>> Setup => Arb.From(PhiAccrualSetup.Gen());

        #region Setups

        private class PhiAccrualSetup : Setup<PhiAccrualState, PhiAccrualModel>
        {
            public static Gen<Setup<PhiAccrualState, PhiAccrualModel>> Gen()
            {
                return
                    Arb.Default.PositiveInt()
                        .Generator.Select(x => (Setup<PhiAccrualState, PhiAccrualModel>) new PhiAccrualSetup(x));
            }

            private readonly PositiveInt _sampleSize;

            public PhiAccrualSetup(PositiveInt sampleSize)
            {
                _sampleSize = sampleSize;
            }

            public override PhiAccrualState Actual()
            {
                return new PhiAccrualState() {History = HeartbeatHistory.Apply(_sampleSize.Get)};
            }

            public override PhiAccrualModel Model()
            {
                return new PhiAccrualModel(_sampleSize.Get, ImmutableList<long>.Empty);
            }

            public override string ToString()
            {
                return $"{GetType()}(SampleSize = {_sampleSize.Get})";
            }
        }

        #endregion

        #region Operations

        private class NextInterval : Operation<PhiAccrualState, PhiAccrualModel>
        {
            public static Gen<Operation<PhiAccrualState, PhiAccrualModel>> Generator()
            {
                //Func<Operation<PhiAccrualState, PhiAccrualModel>> producer = () => new NextInterval(MonotonicClock.GetTicks());
                //return Gen.Fresh(producer);
                return
                    Arb.Default.Int64()
                        .Generator.Select(x => (Operation<PhiAccrualState, PhiAccrualModel>) new NextInterval(x));
            }

            private readonly long _next;

            public NextInterval(long next)
            {
                _next = next;
            }

            public override bool Pre(PhiAccrualModel model)
            {
                return _next >= 0;
            }

            public override Property Check(PhiAccrualState actualHistory, PhiAccrualModel model)
            {
                actualHistory.History = actualHistory.History + _next;
                var actual = actualHistory.History;
                var epsilon = 0.01;
                return
                    (Math.Abs(actual.Mean - model.Mean) <= epsilon)
                        .Label($"Means should be equal, but was [{actual.Mean}]; expected [{model.Mean}]")
                        .And((Math.Abs(actual.StdDeviation - model.StdDeviation) <= epsilon).Label($"StdDev should be equal, but was [{actual.StdDeviation}]; expected [{model.StdDeviation}]"))
                        .And((Math.Abs(actual.Variance - model.Variance) <= epsilon).Label($"Variances should be equal, but was [{actual.Variance}]; expected [{model.Variance}]"));
            }

            public override PhiAccrualModel Run(PhiAccrualModel model)
            {
                return model.NextInterval(_next);
            }

            public override string ToString()
            {
                return $"{GetType()}(Interval={_next})";
            }
        }

        #endregion
    }

    public class PhiAccrualModel
    {
        public PhiAccrualModel(int sampleSize, ImmutableList<long> intervals)
        {
            SampleSize = sampleSize;
            Intervals = intervals;
        }

        public int SampleSize { get; }

        public ImmutableList<long> Intervals { get; }

        public long IntervalSum => Intervals.Sum();

        public long SquaredIntervalSum => Intervals.Select(x => (long) Math.Pow(x, 2.0)).Sum();

        public double Mean => (double)IntervalSum / Intervals.Count;

        public double Variance => (double) SquaredIntervalSum / Intervals.Count - (Math.Pow(Mean, 2));

        public double StdDeviation => Math.Sqrt(Variance);

        public PhiAccrualModel NextInterval(long interval)
        {
            if (Intervals.Count == SampleSize)
            {
                return new PhiAccrualModel(SampleSize, Intervals.Skip(1).ToImmutableList().Add(interval));
            }

            return new PhiAccrualModel(SampleSize, Intervals.Add(interval));
        }
    }
}
#endif
