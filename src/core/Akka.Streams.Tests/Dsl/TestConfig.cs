//-----------------------------------------------------------------------
// <copyright file="TestConfig.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Akka.Actor;

namespace Akka.Streams.Tests.Dsl
{
    internal static class TestConfig
    {
        public static IEnumerable<int> RandomTestRange(ActorSystem system)
        {
            var numberOfTestsToRun = system.Settings.Config.GetInt("akka.stream.test.numberOfRandomizedTests", 10);
            return Enumerable.Range(1, numberOfTestsToRun);
        }
    }
}
