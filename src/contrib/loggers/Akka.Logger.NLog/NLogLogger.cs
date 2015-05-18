//-----------------------------------------------------------------------
// <copyright file="NLogLogger.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Event;
using NLog;
using NLogger = global::NLog.Logger;

namespace Akka.Logger.NLog
{
    /// <summary>
    /// This class is used to receive log events and sends them to
    /// the configured NLog logger. The following log events are
    /// recognized: <see cref="Debug"/>, <see cref="Info"/>,
    /// <see cref="Warning"/> and <see cref="Error"/>.
    /// </summary>
    public class NLogLogger : ReceiveActor
    {
        private readonly ILoggingAdapter _log = Context.GetLogger();

        private static void Log(LogEvent logEvent, Action<NLogger> logStatement)
        {
            var logger = LogManager.GetLogger(logEvent.LogClass.FullName);
            logStatement(logger);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="NLogLogger"/> class.
        /// </summary>
        public NLogLogger()
        {
            Receive<Error>(m => Log(m, logger => logger.Error("{0}", m.Message)));
            Receive<Warning>(m => Log(m, logger => logger.Warn("{0}", m.Message)));
            Receive<Info>(m => Log(m, logger => logger.Info("{0}", m.Message)));
            Receive<Debug>(m => Log(m, logger => logger.Debug("{0}", m.Message)));
            Receive<InitializeLogger>(m =>
            {
                _log.Info("NLogLogger started");
                Sender.Tell(new LoggerInitialized());
            });
        }
    }
}

