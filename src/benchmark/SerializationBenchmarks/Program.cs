//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.CompilerServices;
using Akka.Actor;
using Akka.Configuration;
using Akka.Serialization;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

namespace SerializationBenchmarks
{
    class Program
    {
        static void Main(string[] args)
        {
            BenchmarkRunner.Run<SerializationTests>();
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
