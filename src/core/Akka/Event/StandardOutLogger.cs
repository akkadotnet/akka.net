using System;
using Akka.Actor;
using Akka.Util;

namespace Akka.Event
{
    /// <summary>
    ///     Class StandardOutLogger.
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
        ///     Gets the provider.
        /// </summary>
        /// <value>The provider.</value>
        /// <exception cref="System.Exception">StandardOutLogged does not provide</exception>
        public override ActorRefProvider Provider
        {
            get { throw new Exception("StandardOutLogger does not provide"); }
        }

        public override ActorPath Path
        {
            get { return _path; }
        }

        /// <summary>
        ///     Tells the internal.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="sender">The sender.</param>
        /// <exception cref="System.ArgumentNullException">message</exception>
        protected override void TellInternal(object message, IActorRef sender)
        {
            if(message == null)
                throw new ArgumentNullException("message");
            var logEvent = message as LogEvent;
            if(logEvent != null)
                PrintLogEvent(logEvent);
            else
                Console.WriteLine(message);
        }




        public static ConsoleColor DebugColor { get; set; }
        public static ConsoleColor InfoColor { get; set; }
        public static ConsoleColor WarningColor { get; set; }
        public static ConsoleColor ErrorColor { get; set; }
        public static bool UseColors { get; set; }

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
