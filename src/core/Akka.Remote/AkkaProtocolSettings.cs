﻿//-----------------------------------------------------------------------
// <copyright file="AkkaProtocolSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Configuration;

namespace Akka.Remote
{
    public class AkkaProtocolSettings
    {
        public Config TransportFailureDetectorConfig { get; private set; }

        public string TransportFailureDetectorImplementationClass { get; private set; }

        public TimeSpan TransportHeartBeatInterval { get; private set; }

        public AkkaProtocolSettings(Config config)
        {
            TransportFailureDetectorConfig = config.GetConfig("akka.remote.transport-failure-detector");
            TransportFailureDetectorImplementationClass = TransportFailureDetectorConfig.GetString("implementation-class");
            TransportHeartBeatInterval = TransportFailureDetectorConfig.GetTimeSpan("heartbeat-interval");
        }
    }
}

