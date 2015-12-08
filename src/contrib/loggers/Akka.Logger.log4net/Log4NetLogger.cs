//-----------------------------------------------------------------------
// <copyright file="Log4NetLogger.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Event;
using log4net;

namespace Akka.Logger.log4net
{
    /// <summary>
    /// This class is used to receive log events and sends them to
    /// the configured log4net logger. The following log events are
    /// recognized: <see cref="Debug"/>, <see cref="Info"/>,
    /// <see cref="Warning"/> and <see cref="Error"/>.
    /// </summary>
    public class Log4NetLogger : ReceiveActor
    {
        private readonly ILoggingAdapter _log = Context.GetLogger();

        private static void Log(LogEvent logEvent, Action<ILog> logStatement)
        {
            var logger = LogManager.GetLogger(logEvent.LogClass.FullName);
            logStatement(logger);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Log4NetLogger"/> class.
        /// </summary>
        public Log4NetLogger()
        {
            Receive<Error>(m => Log(m, logger => logger.Error(string.Format("{0}", m.Message), m.Cause)));
            Receive<Warning>(m => Log(m, logger => logger.WarnFormat("{0}", m.Message)));
            Receive<Info>(m => Log(m, logger => logger.InfoFormat("{0}", m.Message)));
            Receive<Debug>(m => Log(m, logger => logger.DebugFormat("{0}", m.Message)));
            Receive<InitializeLogger>(m =>
            {
                _log.Info("log4net started");
                Sender.Tell(new LoggerInitialized());
            });
        }
    }
}
