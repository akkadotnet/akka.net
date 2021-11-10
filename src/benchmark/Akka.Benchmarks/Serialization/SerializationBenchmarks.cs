//-----------------------------------------------------------------------
// <copyright file="SerializationBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
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
        public const int LargeOperations = 1_000;

        private ExtendedActorSystem system;
        private NewtonSoftJsonSerializer json;
        private Message message;
        private LargeMessage largeMessage;

        [GlobalSetup]
        public void Setup()
        {
            system = (ExtendedActorSystem)ActorSystem.Create("system");
            json = new NewtonSoftJsonSerializer(system);
            var actorRef = system.ActorOf<Echo>();
            message = new Message(123, Guid.NewGuid().ToString("D"), actorRef);

            var largeNumbers = new List<int>(Enumerable.Range(0, 1_000));
            var tail = Enumerable.Range(0, 2)
                .Select(_ => new LargeMessage(largeNumbers, new List<LargeMessage>()))
                .ToList();
            largeMessage = new LargeMessage(largeNumbers, tail);
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

        [Benchmark(OperationsPerInvoke = Operations)]
        public void Json_serializer_message_to_binary()
        {
            for (int i = 0; i < Operations; i++)
            {
                json.ToBinary(message);
            }
        }

        [Benchmark(OperationsPerInvoke = Operations)]
        public void Json_serializer_message_from_binary()
        {
            var bytes = json.ToBinary(message);
            for (int i = 0; i < Operations; i++)
            {
                json.FromBinary(bytes, typeof(Message));
            }
        }

        [Benchmark(OperationsPerInvoke = LargeOperations)]
        public void Json_serializer_large_message_roundtrip()
        {
            for (int i = 0; i < LargeOperations; i++)
            {
                var bytes = json.ToBinary(largeMessage);
                json.FromBinary(bytes, typeof(LargeMessage));
            }
        }

        [Benchmark(OperationsPerInvoke = LargeOperations)]
        public void Json_serializer_large_message_to_binary()
        {
            for (int i = 0; i < LargeOperations; i++)
            {
                json.ToBinary(largeMessage);
            }
        }

        [Benchmark(OperationsPerInvoke = LargeOperations)]
        public void Json_serializer_large_message_from_binary()
        {
            var bytes = json.ToBinary(largeMessage);
            for (int i = 0; i < LargeOperations; i++)
            {
                json.FromBinary(bytes, typeof(LargeMessage));
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

        public sealed class LargeMessage
        {
            public LargeMessage(List<int> numbers, List<LargeMessage> tail)
            {
                Numbers = numbers;
                Tail = tail;
            }

            public List<int> Numbers { get; }
            public List<LargeMessage> Tail { get; }
        }

        #endregion
    }
}
