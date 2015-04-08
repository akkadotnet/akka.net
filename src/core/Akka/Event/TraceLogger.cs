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
            PatternMatch.Match(message)
                 .With<InitializeLogger>(m => Sender.Tell(new LoggerInitialized()))
                 .With<Error>(m =>
                      Trace.TraceError(m.ToString()))
                  .With<Warning>(m => Trace.TraceWarning(m.ToString()))
                 .With<DeadLetter>(m => Trace.TraceWarning(string.Format("Deadletter - unable to send message {0} from {1} to {2}", m.Message, m.Sender, m.Sender), typeof(DeadLetter).ToString()))
                 .With<UnhandledMessage>(m => Trace.TraceWarning(string.Format("Unhandled message!"), typeof(UnhandledMessage).ToString()))
                 .Default(m =>
                 {
                     if (m != null)
                         Trace.TraceInformation(m.ToString());
                 });
        }
    }
}
