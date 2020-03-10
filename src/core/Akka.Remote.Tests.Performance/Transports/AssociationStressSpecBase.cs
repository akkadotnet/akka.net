//-----------------------------------------------------------------------
// <copyright file="AssociationStressSpecBase.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.Transport;
using Akka.Util.Internal;
using NBench;

namespace Akka.Remote.Tests.Performance.Transports
{
    /// <summary>
    ///     Creates and shuts associations between two <see cref="ActorSystem" /> instances rapidly
    ///     using pluggable transports. Designed to expose race conditions, deadlocks, and other
    ///     faults with the <see cref="AssociationHandle" /> implementation specific to each transport.
    /// </summary>
    public abstract class AssociationStressSpecBase
    {
        private const string AssociationCounterName = "AssociationConfirmed";
        private static readonly AtomicCounter ActorSystemNameCounter = new AtomicCounter(0);
        protected readonly TimeSpan AssociationTimeout = TimeSpan.FromSeconds(2);
        protected Counter AssociationCounter;

        protected Props ActorProps = Props.Create(() => new EchoActor());

        /// <summary>
        ///     Used to create a HOCON <see cref="Config" /> object for each <see cref="ActorSystem" />
        ///     participating in this throughput test.
        ///     This method is responsible for selecting the correct <see cref="Transport" /> implementation.
        /// </summary>
        /// <param name="actorSystemName">The name of the <see cref="ActorSystem" />. Needed for <see cref="TestTransport" />.</param>
        /// <param name="ipOrHostname">The address this system will be bound to</param>
        /// <param name="port">The port this system will be bound to</param>
        /// <param name="registryKey">
        ///     The <see cref="AssociationRegistry" /> key. Only needed when using
        ///     <see cref="TestTransport" />.
        /// </param>
        /// <returns>A populated <see cref="Config" /> object.</returns>
        public abstract Config CreateActorSystemConfig(string actorSystemName, string ipOrHostname, int port,
            string registryKey = null);

        public virtual string CreateRegistryKey()
        {
            return null;
        }

        private class EchoActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                Sender.Tell(message);
            }
        }

        [PerfSetup]
        public void Setup(BenchmarkContext context)
        {
            AssociationCounter = context.GetCounter(AssociationCounterName);
        }

        [PerfBenchmark(
            Description =
                "Tests how quickly and frequently we can make associations between two ActorSystems using the configured transport",
            NumberOfIterations = 13, RunTimeMilliseconds = 2000, RunMode = RunMode.Throughput,
            TestMode = TestMode.Measurement)]
        [CounterMeasurement(AssociationCounterName)]
        public void AssociationStress(BenchmarkContext context)
        {
            var registryKey = CreateRegistryKey();
            using (
                var system1 = ActorSystem.Create("SystemA" + ActorSystemNameCounter.Next(),
                    CreateActorSystemConfig("SystemA" + ActorSystemNameCounter.Current, "127.0.0.1", 0, registryKey)))
            using (
                var system2 = ActorSystem.Create("SystemB" + ActorSystemNameCounter.Next(),
                    CreateActorSystemConfig("SystemB" + ActorSystemNameCounter.Current, "127.0.0.1", 0, registryKey)))
            {
                var echo = system1.ActorOf(ActorProps, "echo");
                var system1Address = RARP.For(system1).Provider.Transport.DefaultAddress;
                var system1EchoActorPath = new RootActorPath(system1Address) / "user" / "echo";

               var remoteActor = system2.ActorSelection(system1EchoActorPath)
                   .Ask<ActorIdentity>(new Identify(null), TimeSpan.FromSeconds(2)).Result.Subject;
                AssociationCounter.Increment();
            }
        }

        [PerfCleanup]
        public virtual void Cleanup()
        {
        }
    }
}
