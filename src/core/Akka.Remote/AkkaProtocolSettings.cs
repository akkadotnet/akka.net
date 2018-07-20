//-----------------------------------------------------------------------
// <copyright file="AkkaProtocolSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Configuration;

namespace Akka.Remote
{
    /// <summary>
    /// TBD
    /// </summary>
    public class AkkaProtocolSettings
    {
        /// <summary>
        /// TBD
        /// </summary>
        public Config TransportFailureDetectorConfig { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public string TransportFailureDetectorImplementationClass { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan TransportHeartBeatInterval { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="config">TBD</param>
        public AkkaProtocolSettings(Config config)
        {
            TransportFailureDetectorConfig = config.GetConfig("akka.remote.transport-failure-detector");
            TransportFailureDetectorImplementationClass = TransportFailureDetectorConfig.GetString("implementation-class");
            TransportHeartBeatInterval = TransportFailureDetectorConfig.GetTimeSpan("heartbeat-interval");
        }
    }
}

