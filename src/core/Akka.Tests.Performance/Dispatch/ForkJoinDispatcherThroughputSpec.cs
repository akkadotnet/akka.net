//-----------------------------------------------------------------------
// <copyright file="ForkJoinDispatcherThroughputSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Dispatch;

namespace Akka.Tests.Performance.Dispatch
{
    public class ForkJoinDispatcherColdThroughputSpec : ColdDispatcherThroughputSpecBase
    {
        public static Config DispatcherConfiguration => ConfigurationFactory.ParseString(@"
                    id = PerfTest
                    executor = fork-join-executor
                    dedicated-thread-pool{ #settings for Helios.DedicatedThreadPool
                        thread-count = 3 
                        #deadlock-timeout = 3s 
                        threadtype = background 
                    }
        ");

        protected override MessageDispatcherConfigurator Configurator()
        {
            return new DispatcherConfigurator(DispatcherConfiguration, Prereqs);
        }
    }

    public class ForkJoinDispatcherWarmThroughputSpec : WarmDispatcherThroughputSpecBase
    {
       public static Config DispatcherConfiguration => ConfigurationFactory.ParseString(@"
                    id = PerfTest
                    executor = fork-join-executor
                    dedicated-thread-pool{ #settings for Helios.DedicatedThreadPool
                        thread-count = 3 
                        #deadlock-timeout = 3s 
                        threadtype = background 
                    }
        ");

        protected override MessageDispatcherConfigurator Configurator()
        {
            return new DispatcherConfigurator(DispatcherConfiguration, Prereqs);
        }
    }
}
