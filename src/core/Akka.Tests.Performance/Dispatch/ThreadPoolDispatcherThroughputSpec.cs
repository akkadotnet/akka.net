﻿//-----------------------------------------------------------------------
// <copyright file="ThreadPoolDispatcherThroughputSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
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