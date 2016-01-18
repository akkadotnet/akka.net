//-----------------------------------------------------------------------
// <copyright file="CommonLoggingLogger.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

using Akka.Actor;
using Akka.Event;

using Common.Logging;

namespace Akka.Logger.CommonLogging
{
    /// <summary>
    /// This class is used to receive log events and sends them to
    /// the configured common logging. The following log events are
    /// recognized: <see cref="Debug"/>, <see cref="Info"/>,
    /// <see cref="Warning"/> and <see cref="Error"/>.
    /// </summary>
    public class CommonLoggingLogger : ReceiveActor
    {
        private readonly ILoggingAdapter _log = Context.GetLogger();

        /// <summary>
        /// Initializes a new instance of the <see cref="CommonLoggingLogger"/> class.
        /// </summary>
        public CommonLoggingLogger()
        {
            this.Receive<Error>(m => Log(m, logger => logger.Error(string.Format("{0}", m.Message), m.Cause)));
            this.Receive<Warning>(m => Log(m, logger => logger.WarnFormat("{0}", m.Message)));
            this.Receive<Info>(m => Log(m, logger => logger.InfoFormat("{0}", m.Message)));
            this.Receive<Debug>(m => Log(m, logger => logger.DebugFormat("{0}", m.Message)));
            this.Receive<InitializeLogger>(m =>
            {
                this._log.Info("CommonLoggingLogger started");
                this.Sender.Tell(new LoggerInitialized());
            });
        }
        private static void Log(LogEvent logEvent, Action<ILog> logStatement)
        {
            var logger = LogManager.GetLogger(logEvent.LogClass.FullName);
            logStatement(logger);
        }
    }
}