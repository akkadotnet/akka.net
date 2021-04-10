//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Serialization;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Newtonsoft.Json;

namespace SerializationBenchmarks
{
    class Program
    {
        static void Main(string[] args)
        {
            BenchmarkRunner.Run<JsonSerializerTests>();
        }
    }

    public class TestSer
    {
        public int Id { get; set; }
        public string someStr { get; set; }
        public string someStr2 { get; set; }
        public string someStr3 { get; set; }
        public Guid IDK { get; set; }
    }
        [MemoryDiagnoser]
    public class JsonSerializerTests
    {
        public JsonSerializerTests()
        {
            _sys_noPool = ActorSystem.Create("bench-serialization-json-nopool",ConfigurationFactory.ParseString(@"
akka.actor {{
                        
serialization-settings {{
  json {{
    use-pooled-string-builder = false
 }}
}}
                    }}"));
            _sys_pool = ActorSystem.Create("bench-serialization-json-pool");
            _noPoolSer =
                _sys_noPool.Serialization.FindSerializerForType(typeof(object));
            _poolSer = 
            _sys_pool.Serialization.FindSerializerForType(typeof(object));
        }
        private static TestSer testObj = new TestSer()
        {
            Id = 124,
            someStr =
                "412tgieoargj4a9349u2u-03jf3290rjf2390ja209fj1099u42n0f92qm93df3m-032jfq-102",
            someStr2 =
                "412tgieoargj4a9349u2u-03jf3290rjf2390ja209fj1099u42n0f92qm93df3m-032jfq-102",
            someStr3 =
                new string(Enumerable.Repeat('l',512).ToArray()),
            IDK = Guid.Empty
        };

        private ActorSystem _sys_noPool;
        private ActorSystem _sys_pool;
        private Serializer _noPoolSer;
        private Serializer _poolSer;
        private const int _numIters = 10000; 
        [Benchmark]
        public void Pooling()
        {
            for (int i = 0; i < _numIters; i++)
            {
                _poolSer.ToBinary(testObj);
            }
        }
        [Benchmark]
        public void NoPooling()
        {
            for (int i = 0; i < _numIters; i++)
            {
                _noPoolSer.ToBinary(testObj);
            }
        }
        
        [Benchmark]
        public void Pooling_MultiTasks()
        {
            Task.WaitAll(Enumerable.Repeat(0, 10)
                .Select((l) => Task.Run(Pooling)).ToArray());
        }
        [Benchmark]
        public void NoPooling_MultiTasks()
        {
            Task.WaitAll(Enumerable.Repeat(0, 10)
                .Select((l) => Task.Run(NoPooling)).ToArray());
        }
    }

    //[MemoryDiagnoser]
    public class SerializationTests
    {
        private ActorSystem _sys;
        private Serializer _ser;

        public SerializationTests()
        {
            _sys = ActorSystem.Create("bench-serialization",ConfigurationFactory.ParseString(@"
akka.actor {{
                        serializers {{
                            hyperion = ""Akka.Serialization.HyperionSerializer, Akka.Serialization.Hyperion""
                        }}
                        serialization-bindings {{
                            ""System.Object"" = hyperion
                        }}
                    }}"));
            _ser = _sys.Serialization.FindSerializerForType(typeof(MyType));
        }
        
        public static MyType payload =>
            new MyType() { SomeInt = 1, SomeStr = "lol" };
        [Benchmark]
        public void Serialization_WithTransport_NoState()
        {
            DoSer_NoState(payload);
        }

        //Noninlining here because we don't want
        //Jitter to inline and possibly remove the capture of 2 variables
        //we want to simulate in this benchmark
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void DoSer_NoState(MyType thisPayload)
        {
            var res = Akka.Serialization.Serialization.WithTransport(
                _sys.Serialization.System,
                () =>
                {
                    return _ser.ToBinary(thisPayload);
                });
        }

        //Noinlining here to be fair, since we are noinling on other.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void DoSer_State(MyType thisPayload)
        {
            var res = Akka.Serialization.Serialization.WithTransport(
                _sys.Serialization.System,(_ser,thisPayload),
                (inp) =>
                {
                    var (serializer, myType) = inp;
                    return serializer.ToBinary(myType);
                });
        }
        
        [Benchmark]
        public void Serialization_WithTransport_State()
        {
            DoSer_State(payload);
        }
    }

    public class MyType
    {
        public string SomeStr { get; set; }
        public int SomeInt { get; set; }
    }
}
