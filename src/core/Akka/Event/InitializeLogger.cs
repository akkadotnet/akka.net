//-----------------------------------------------------------------------
// <copyright file="InitializeLogger.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.Event
{
    /// <summary>
    /// This class represents a message used to initialize a logger.
    /// </summary> 
    public class InitializeLogger : INoSerializationVerificationNeeded
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="InitializeLogger" /> message.
        /// </summary>
        /// <param name="loggingBus">The bus used by the logger to log events.</param>
        public InitializeLogger(LoggingBus loggingBus)
        {
            LoggingBus = loggingBus;
        }

        /// <summary>
        /// The bus used by the logger to log events.
        /// </summary>
        public LoggingBus LoggingBus { get; private set; }
    }
}
