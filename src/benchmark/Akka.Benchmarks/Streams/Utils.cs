#region copyright
// -----------------------------------------------------------------------
//  <copyright file="Utils.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------
#endregion

using System;
using System.Threading;
using Akka.Streams;
using Akka.Streams.Stage;

namespace Akka.Benchmarks.Streams
{
    internal sealed class LatchSink<T> : GraphStage<SinkShape<T>>
    {
        #region logic

        private sealed class Logic : InGraphStageLogic
        {
            private readonly Inlet<T> inlet;
            private readonly CountdownEvent latch;
            private readonly int countDownAfter;

            private int n = 0;

            public Logic(LatchSink<T> stage) : base(stage.Shape)
            {
                this.inlet = stage.Inlet;
                this.latch = stage.latch;
                this.countDownAfter = stage.countDownAfter;

                SetHandler(this.inlet, this);
            }

            public override void OnPush()
            {
                n++;
                if (n == countDownAfter)
                    latch.Signal();

                Grab(inlet);
                Pull(inlet);
            }

            public override void PreStart() => Pull(inlet);
        }

        #endregion

        private readonly int countDownAfter;
        private readonly CountdownEvent latch;

        public LatchSink(int countDownAfter, CountdownEvent latch)
        {
            this.countDownAfter = countDownAfter;
            this.latch = latch;
            Shape = new SinkShape<T>(Inlet);
        }

        public Inlet<T> Inlet { get; } = new Inlet<T>("latchSink.in");

        public override SinkShape<T> Shape { get; }
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
    }
}