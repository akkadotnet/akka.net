//-----------------------------------------------------------------------
// <copyright file="NameAndUidBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Actor
{
    [Config(typeof(MicroBenchmarkConfig))]
    public class NameAndUidBenchmarks
    {
        public const string ActorPath = "foo#11241311";

        [Benchmark]
        public NameAndUid ActorCell_SplitNameAndUid()
        {
            return ActorCell.SplitNameAndUid(ActorPath);
        }

        [Benchmark]
        public (string name, int uid) ActorCell_GetNameAndUid()
        {
            return ActorCell.GetNameAndUid(ActorPath);
        }
    }
}
