//-----------------------------------------------------------------------
// <copyright file="ActorPathBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Actor
{
    [Config(typeof(MicroBenchmarkConfig))]
    public class ActorPathBenchmarks
    {
        private ActorPath x;
        private ActorPath y;
        private ActorPath _childPath;
        private Address _sysAdr = new Address("akka.tcp", "system", "127.0.0.1", 1337);
        private Address _otherAdr = new Address("akka.tcp", "system", "127.0.0.1", 1338);

        private string _actorPathStr;
        
        [Params(1, 100000, int.MaxValue)]
        public int Uid { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            x = new RootActorPath(_sysAdr, "user");
            y = new RootActorPath(_sysAdr, "system");
            var parentPath = x / "parent";
            _childPath = new ChildActorPath(parentPath, "child", Uid);
            _actorPathStr = _childPath.ToSerializationFormat();
        }

        [Benchmark]
        public ActorPath ActorPath_Parse()
        {
            return ActorPath.Parse(_actorPathStr);
        }

        [Benchmark]
        public ActorPath ActorPath_Concat()
        {
            return x / "parent" / "child";
        }
        
        [Benchmark]
        public bool ActorPath_Equals()
        {
            return x == y;
        }

        [Benchmark]
        public string ActorPath_ToString()
        {
            return _childPath.ToString();
        }

        [Benchmark]
        public string ActorPath_ToSerializationFormat()
        {
            return _childPath.ToSerializationFormat();
        }

        [Benchmark]
        public string ActorPath_ToSerializationFormatWithAddress()
        {
            return _childPath.ToSerializationFormatWithAddress(_otherAdr);
        }
    }
}
