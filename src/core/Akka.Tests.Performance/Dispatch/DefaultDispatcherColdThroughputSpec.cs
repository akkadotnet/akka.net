//-----------------------------------------------------------------------
// <copyright file="DefaultDispatcherColdThroughputSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Dispatch;

namespace Akka.Tests.Performance.Dispatch
{

    public class ThreadPoolDispatcherColdThroughputSpec : ColdDispatcherThroughputSpecBase
    {
        public static Config DispatcherConfiguration => ConfigurationFactory.ParseString(@"
                    id = PerfTest
                    executor = default-executor
        ");

        protected override MessageDispatcherConfigurator Configurator()
        {
            return new DispatcherConfigurator(DispatcherConfiguration, Prereqs);
        }
    }

    public class ThreadPoolDispatcherWarmThroughputSpec : WarmDispatcherThroughputSpecBase
    {
        public static Config DispatcherConfiguration => ConfigurationFactory.ParseString(@"
                    id = PerfTest
                    executor = default-executor
        ");

        protected override MessageDispatcherConfigurator Configurator()
        {
            return new DispatcherConfigurator(DispatcherConfiguration, Prereqs);
        }
    }
}
