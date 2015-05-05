//-----------------------------------------------------------------------
// <copyright file="StandardOutLogger.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Util;

namespace Akka.Event
{
    /// <summary>
    /// Represents a logger that logs using the StandardOutWriter.
    /// The logger can also be configured to use colors for the various log event types.
    /// </summary>
    public class StandardOutLogger : MinimalActorRef
    {
        private readonly ActorPath _path = new RootActorPath(Address.AllSystems, "/StandardOutLogger");

        static StandardOutLogger()
        {
            DebugColor = ConsoleColor.Gray;
            InfoColor = ConsoleColor.White;
            WarningColor = ConsoleColor.Yellow;
            ErrorColor = ConsoleColor.Red;
            UseColors = true;
        }
        
        /// <summary>
        /// Gets the provider.
        /// </summary>
        /// <value>The provider.</value>
        /// <exception cref="System.Exception">StandardOutLogged does not provide</exception>
        public override IActorRefProvider Provider
        {
            get { throw new Exception("StandardOutLogger does not provide"); }
        }

        /// <summary>
        /// Gets the path of this actor.
        /// </summary>
        public override ActorPath Path
        {
            get { return _path; }
        }

        /// <summary>
        /// Handles log events printing them to the Console.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="sender">The sender.</param>
        /// <exception cref="System.ArgumentNullException">message</exception>
        protected override void TellInternal(object message, IActorRef sender)
        {
            if(message == null)
                throw new ArgumentNullException("message");

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
        /// Gets or Sets the color of Debug events.
        /// </summary>
        public static ConsoleColor DebugColor { get; set; }

        /// <summary>
        /// Gets or Sets the color of Info events.
        /// </summary>
        public static ConsoleColor InfoColor { get; set; }

        /// <summary>
        /// Gets or Sets the color of Warning events.
        /// </summary>
        public static ConsoleColor WarningColor { get; set; }

        /// <summary>
        /// Gets or Sets the color of Error events. 
        /// </summary>
        public static ConsoleColor ErrorColor { get; set; }

        /// <summary>
        /// Gets or Sets whether or not to use colors when printing events.
        /// </summary>
        public static bool UseColors { get; set; }

        /// <summary>
        /// Prints the LogEvent using the StandardOutWriter.
        /// </summary>
        /// <param name="logEvent"></param>
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