//-----------------------------------------------------------------------
// <copyright file="DefaultDispatcherColdThroughputSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Dispatch;

namespace Akka.Tests.Performance.Dispatch
{
    public class ThreadPoolDispatcherColdThroughputSpec : ColdDispatcherThroughputSpecBase
    {
        protected override MessageDispatcherConfigurator Configurator()
        {
            return new ThreadPoolDispatcherConfigurator(ConfigurationFactory.Empty, null);
        }
    }

    public class ThreadPoolDispatcherWarmThroughputSpec : WarmDispatcherThroughputSpecBase
    {
        protected override MessageDispatcherConfigurator Configurator()
        {
            return new ThreadPoolDispatcherConfigurator(ConfigurationFactory.Empty, null);
        }
    }
}