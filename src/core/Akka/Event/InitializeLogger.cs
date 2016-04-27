//-----------------------------------------------------------------------
// <copyright file="InitializeLogger.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.Event
{
    /// <summary>
    /// Message used to initialize a logger.
    /// </summary> 
    public class InitializeLogger : INoSerializationVerificationNeeded
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="InitializeLogger" /> message.
        /// </summary>
        /// <param name="loggingBus">The logging bus.</param>
        public InitializeLogger(LoggingBus loggingBus)
        {
            LoggingBus = loggingBus;
        }

        /// <summary>
        /// Gets the logging bus instance.
        /// </summary>
        /// <value>The logging bus instance.</value>
        public LoggingBus LoggingBus { get; private set; }
    }
}

