//-----------------------------------------------------------------------
// <copyright file="Slf4NetLogger.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Event;
using slf4net;

namespace Akka.Logger.slf4net
{
    /// <summary>
    /// This class is used to receive log events and sends them to
    /// the configured slf4net logger. The following log events are
    /// recognized: <see cref="Debug"/>, <see cref="Info"/>,
    /// <see cref="Warning"/> and <see cref="Error"/>.
    /// </summary>
    public class Slf4NetLogger : UntypedActor, IRequiresMessageQueue<ILoggerMessageQueueSemantics>
    {
        private readonly ILoggingAdapter _log = Context.GetLogger();

        private void WithMDC(Action<ILogger> logStatement)
        {
            ILogger logger = LoggerFactory.GetLogger(GetType());
            logStatement(logger);
        }

        /// <summary>
        /// Receives an event and logs it to the slf4net logger.
        /// </summary>
        /// <param name="message">The event sent to the logger.</param>
        protected override void OnReceive(object message)
        {
            message
                .Match()
                .With<Error>(m => WithMDC(logger => logger.Error("{0}", m.Message)))
                .With<Warning>(m => WithMDC(logger => logger.Warn("{0}", m.Message)))
                .With<Info>(m => WithMDC(logger => logger.Info("{0}", m.Message)))
                .With<Debug>(m => WithMDC(logger => logger.Debug("{0}", m.Message)))
                .With<InitializeLogger>(m =>
                {
                    _log.Info("Slf4jLogger started");
                    Sender.Tell(new LoggerInitialized());
                });
        }
    }
}

