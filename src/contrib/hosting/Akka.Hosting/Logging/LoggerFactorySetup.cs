// -----------------------------------------------------------------------
//  <copyright file="LoggerFactorySetup.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using Akka.Actor.Setup;
using Microsoft.Extensions.Logging;

namespace Akka.Hosting.Logging
{
    public class LoggerFactorySetup : Setup
    {
        public LoggerFactorySetup(ILoggerFactory loggerFactory)
        {
            LoggerFactory = loggerFactory;
        }

        public ILoggerFactory LoggerFactory { get; }
    }
}