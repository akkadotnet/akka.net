//-----------------------------------------------------------------------
// <copyright file="TraceLogger.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
            message.Match()
                 .With<InitializeLogger>(m => Sender.Tell(new LoggerInitialized()))
                 .With<Error>(m => Trace.TraceError(m.ToString()))
                 .With<Warning>(m => Trace.TraceWarning(m.ToString()))
                 .With<DeadLetter>(m => Trace.TraceWarning(string.Format("Deadletter - unable to send message {0} from {1} to {2}", m.Message, m.Sender, m.Sender), typeof(DeadLetter).ToString()))
                 .With<UnhandledMessage>(m => Trace.TraceWarning("Unhandled message!"))
                 .Default(m =>
                 {
                     if (m != null)
                         Trace.TraceInformation(m.ToString());
                 });
        }
    }
}
