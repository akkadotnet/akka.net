//-----------------------------------------------------------------------
// <copyright file="TraceLogger.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Diagnostics;
using Akka.Actor;

namespace Akka.Event
{
    /// <summary>
    /// TraceLogger - writes to System.Trace; useful for systems that use trace listeners.
    /// 
    /// To activate the TraceLogger, add loggers = [""Akka.Event.TraceLogger, Akka""] to your config
    /// </summary>
    public class TraceLogger : UntypedActor
    {
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

