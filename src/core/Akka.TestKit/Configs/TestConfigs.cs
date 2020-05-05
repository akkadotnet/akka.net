//-----------------------------------------------------------------------
// <copyright file="TestConfigs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Configuration;

namespace Akka.TestKit.Configs
{
    /// <summary>
    /// Default Akka.TestKit configurations
    /// </summary>
    public static class TestConfigs
    {
        /// <summary>
        /// The default TestKit config
        /// </summary>
        public static Config DefaultConfig
        {
            get { return ConfigurationFactory.FromResource<TestKitBase>("Akka.TestKit.Internal.Reference.conf"); }
        }

        /// <summary>
        /// Configuration for tests that require deterministic control over the AkkaSystem scheduler.
        /// </summary>
        public static Config TestSchedulerConfig
        {
            get { return ConfigurationFactory.FromResource<TestKitBase>("Akka.TestKit.Configs.TestScheduler.conf"); }
        }
    }
}
