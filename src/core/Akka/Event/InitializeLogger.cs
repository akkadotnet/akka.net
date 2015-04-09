//-----------------------------------------------------------------------
// <copyright file="InitializeLogger.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.Event
{
    /// <summary>
    ///     Class InitializeLogger.
    /// </summary> 
    public class InitializeLogger : INoSerializationVerificationNeeded
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="InitializeLogger" /> class.
        /// </summary>
        /// <param name="loggingBus">The logging bus.</param>
        public InitializeLogger(LoggingBus loggingBus)
        {
            LoggingBus = loggingBus;
        }

        /// <summary>
        ///     Gets the logging bus.
        /// </summary>
        /// <value>The logging bus.</value>
        public LoggingBus LoggingBus { get; private set; }
    }
}

