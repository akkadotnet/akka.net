//-----------------------------------------------------------------------
// <copyright file="SerializationBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Serialization;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Serialization
{
    [Config(typeof(MicroBenchmarkConfig))]
    public class SerializationBenchmarks
    {
        public const int Operations = 10_000;

        private ExtendedActorSystem system;
        private NewtonSoftJsonSerializer json;
        private Message message;

        [GlobalSetup]
        public void Setup()
        {
            system = (ExtendedActorSystem)ActorSystem.Create("system");
            json = new NewtonSoftJsonSerializer(system);
            var actorRef = system.ActorOf<Echo>();
            message = new Message(123, Guid.NewGuid().ToString("D"), actorRef);
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            system.Dispose();
        }

        [Benchmark(OperationsPerInvoke = Operations)]
        public void Json_serializer_message_roundtrip()
        {
            for (int i = 0; i < Operations; i++)
            {
                var bytes = json.ToBinary(message);
                json.FromBinary(bytes, typeof(Message));
            }
        }

        #region messages

        sealed class Echo : ReceiveActor
        {
            public Echo()
            {
                ReceiveAny(msg => Sender.Tell(msg));
            }
        }

        public sealed class Message
        {
            public int Id { get; }
            public string Name { get; }
            public IActorRef ActorRef { get; }

            public Message(int id, string name, IActorRef actorRef)
            {
                Id = id;
                Name = name;
                ActorRef = actorRef;
            }
        }

        #endregion
    }
}
