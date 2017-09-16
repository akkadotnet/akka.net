//-----------------------------------------------------------------------
// <copyright file="StandardOutLogger.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Util;

namespace Akka.Event
{
    /// <summary>
    /// This class represents an event logger that logs its messages to standard output (e.g. the console).
    /// 
    /// <remarks>
    /// This logger is always attached first in order to be able to log failures during application start-up,
    /// even before normal logging is started.
    /// </remarks>
    /// </summary>
    public class StandardOutLogger : MinimalActorRef
    {
        private readonly ActorPath _path = new RootActorPath(Address.AllSystems, "/StandardOutLogger");

        /// <summary>
        /// Initializes the <see cref="StandardOutLogger"/> class.
        /// </summary>
        static StandardOutLogger()
        {
            DebugColor = ConsoleColor.Gray;
            InfoColor = ConsoleColor.White;
            WarningColor = ConsoleColor.Yellow;
            ErrorColor = ConsoleColor.Red;
            UseColors = true;
        }

        /// <summary>
        /// N/A
        /// </summary>
        /// <exception cref="NotImplementedException">This exception is automatically thrown since <see cref="StandardOutLogger"/> does not support this property.</exception>
        public override IActorRefProvider Provider
        {
            get { throw new NotSupportedException("This logger does not provide."); }
        }

        /// <summary>
        /// The path where this logger currently resides.
        /// </summary>
        public override ActorPath Path
        {
            get { return _path; }
        }

        /// <summary>
        /// Handles incoming log events by printing them to the console.
        /// </summary>
        /// <param name="message">The message to print</param>
        /// <param name="sender">The actor that sent the message.</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown if the given <paramref name="message"/> is undefined.
        /// </exception>
        protected override void TellInternal(object message, IActorRef sender)
        {
            if(message == null)
                throw new ArgumentNullException(nameof(message), "The message to log must not be null.");

            var logEvent = message as LogEvent;
            if (logEvent != null)
            {
                PrintLogEvent(logEvent);
            }
            else
            {
                Console.WriteLine(message);
            }
        }
        
        /// <summary>
        /// The foreground color to use when printing Debug events to the console.
        /// </summary>
        public static ConsoleColor DebugColor { get; set; }

        /// <summary>
        /// The foreground color to use when printing Info events to the console.
        /// </summary>
        public static ConsoleColor InfoColor { get; set; }

        /// <summary>
        /// The foreground color to use when printing Warning events to the console.
        /// </summary>
        public static ConsoleColor WarningColor { get; set; }

        /// <summary>
        /// The foreground color to use when printing Error events to the console.
        /// </summary>
        public static ConsoleColor ErrorColor { get; set; }

        /// <summary>
        /// Determines whether colors are used when printing events to the console. 
        /// </summary>
        public static bool UseColors { get; set; }

        /// <summary>
        /// Prints a specified event to the console.
        /// </summary>
        /// <param name="logEvent">The event to print</param>
        public static void PrintLogEvent(LogEvent logEvent)
        {
            ConsoleColor? color = null;
            
            if(UseColors)
            {
                var logLevel = logEvent.LogLevel();
                switch(logLevel)
                {
                    case LogLevel.DebugLevel:
                        color = DebugColor;
                        break;
                    case LogLevel.InfoLevel:
                        color = InfoColor;
                        break;
                    case LogLevel.WarningLevel:
                        color = WarningColor;
                        break;
                    case LogLevel.ErrorLevel:
                        color = ErrorColor;
                        break;
                }
            }

            StandardOutWriter.WriteLine(logEvent.ToString(), color);
        }
    }
}
