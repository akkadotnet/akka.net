using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka;
using Akka.Actor;

namespace Akka.Event
{
    /// <summary>
    ///     Class DefaultLogger.
    /// </summary>
    public class DefaultLogger : ActorBase
    {
        /// <summary>
        ///     Processor for user defined messages.
        /// </summary>
        /// <param name="message">The message.</param>
        protected override bool Receive(object message)
        {
            if(message is InitializeLogger)
            {
                Sender.Tell(new LoggerInitialized());
                return true;
            }
            var logEvent = message as LogEvent;
            if(logEvent != null)
            {
                Print(logEvent);
                return true;
            }
            return false;            
        }

        protected virtual void Print(LogEvent m)
        {
            Console.WriteLine(m);
        }
    }
}
