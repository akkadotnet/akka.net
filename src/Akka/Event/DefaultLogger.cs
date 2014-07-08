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
    public class DefaultLogger : UntypedActor
    {
        /// <summary>
        ///     Processor for user defined messages.
        /// </summary>
        /// <param name="message">The message.</param>
        protected override void OnReceive(object message)
        {
            message
                .Match()
                .With<InitializeLogger>(m => Sender.Tell(new LoggerInitialized()))
                .With<LogEvent>(m =>
                    Console.WriteLine(m))
                .Default(Unhandled);
        }
    }
}
