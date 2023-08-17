//-----------------------------------------------------------------------
// <copyright file="ClusterSpecBase.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Configuration;
using Akka.TestKit;
using Xunit.Abstractions;

namespace Akka.Cluster.Tests
{
    /// <summary>
    /// Abstract base class for cluster specs - turns on required serialization properties
    /// </summary>
    public abstract class ClusterSpecBase : AkkaSpec
    {
        protected ClusterSpecBase(Config config, ITestOutputHelper output, bool useLegacyHeartbeat) 
            : base(config.WithFallback(BaseConfig(useLegacyHeartbeat)), output)
        {
            
        }

        protected ClusterSpecBase(ITestOutputHelper output, bool useLegacyHeartbeat)
            : base(BaseConfig(useLegacyHeartbeat), output)
        {

        }

        private static Config BaseConfig(bool useLegacyHeartbeat) => 
            ConfigurationFactory.ParseString($@"
                akka.actor.serialize-messages = on
                akka.actor.serialize-creators = on
                akka.cluster.use-legacy-heartbeat-message = {(useLegacyHeartbeat ? "true" : "false")}");
    }
}

