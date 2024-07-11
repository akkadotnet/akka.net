//-----------------------------------------------------------------------
// <copyright file="TraceLogger.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Diagnostics;
using Akka.Actor;

namespace Akka.Event
{
    /// <summary>
    /// This class represents an event logger that logs its messages using a configured trace listener.
    /// 
    /// <remarks>
    /// To use activate this logger, modify the loggers section in your Akka.NET configuration like so,
    /// 
    /// <code>
    /// akka {
    ///   ...
    ///   loggers = [""Akka.Event.TraceLogger, Akka""]
    ///   ...
    /// }
    /// </code>
    /// 
    /// Further configuration may be required in your main configuration (e.g. app.config or web.config)
    /// to properly set up the trace. See <see href="https://msdn.microsoft.com/en-us/library/zs6s4h68.aspx">here</see>
    /// for more information regarding .NET tracing.
    /// </remarks>
    /// </summary>
    public class TraceLogger : UntypedActor
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case InitializeLogger _:
                    Sender.Tell(new LoggerInitialized());
                    break;
                case Error m:
                    Trace.TraceError(m.ToString());
                    break;
                case Warning m:
                    Trace.TraceWarning(m.ToString());
                    break;
                case DeadLetter m:
                    Trace.TraceWarning($"Deadletter - unable to send message {m.Message} from {m.Sender} to {m.Sender}", typeof(DeadLetter));
                    break;
                case UnhandledMessage _:
                    Trace.TraceWarning("Unhandled message!");
                    break;
                default:
                    if (message != null)
                        Trace.TraceInformation(message.ToString());
                    break;
            }
        }
    }
}
