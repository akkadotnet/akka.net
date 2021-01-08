//-----------------------------------------------------------------------
// <copyright file="ActorSystemShutdownSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Util;
using NBench;

namespace Akka.Tests.Performance.Actor
{
    /// <summary>
    /// Measures the speed at which we can shut down various-sized <see cref="ActorSystem"/> instances.
    /// </summary>
    public abstract class ActorSystemShutdownSpec
    {
        private class TerminatorActor : ReceiveActor { }

        private readonly int _actorCount;
        private ActorSystem _actorSystem;
        private static readonly Props _terminatorProps = Props.Create<TerminatorActor>();

        protected ActorSystemShutdownSpec(int actorCount)
        {
            _actorCount = actorCount;
        }

        [PerfSetup]
        public void SetUp()
        {
            _actorSystem = ActorSystem.Create("ActorSystemShutdownSpec" + ThreadLocalRandom.Current.Next());
            for (var i = 0; i < _actorCount; i++)
                _actorSystem.ActorOf(_terminatorProps);
        }

        [PerfBenchmark(Description = "Times how long it takes for an ActorSystem with N actors to shut down.", NumberOfIterations = 13, RunMode = RunMode.Iterations, TestMode = TestMode.Measurement)]
        [TimingMeasurement]
        public void ActorSystemShutdownTime()
        {
            _actorSystem.Terminate().Wait();
        }
    }

    public class ActorSystemShutdown1000Spec : ActorSystemShutdownSpec
    {
        public ActorSystemShutdown1000Spec() : base(1000)
        {
        }
    }
    public class ActorSystemShutdown20000Spec : ActorSystemShutdownSpec {
        public ActorSystemShutdown20000Spec() : base(20000)
        {
        }
    }

    public class ActorSystemShutdown100000Spec : ActorSystemShutdownSpec
    {
        public ActorSystemShutdown100000Spec() : base(100000)
        {
        }
    }
}
