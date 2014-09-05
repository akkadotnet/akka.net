using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;

namespace Akka.TestKit
{
    public abstract class EventFilter
    {
        protected int Occurrences;
        protected string Source;
        protected string Message;
        protected bool Complete;

        protected EventFilter(int occurrences)
        {
            Occurrences = occurrences;
            Message = string.Empty;
            Complete = false;
        }

        protected EventFilter(int occurrences, string message, string source, bool complete)
        {
            Occurrences = occurrences;
            Message = message;
            Source = source;
            Complete = complete;
        }

        protected EventFilter() : this(int.MaxValue)
        {
        }

        protected abstract bool IsMatch(LogEvent evt);

        public bool Apply(LogEvent evt)
        {
            if (IsMatch(evt))
            {
                if(Occurrences != int.MaxValue) Interlocked.Decrement(ref Occurrences);
                return true;
            }

            return false;
        }

        public bool AwaitDone(TimeSpan timeout)
        {
            if (Occurrences != int.MaxValue && Occurrences > 0)
            {
                var t = Task.Factory.StartNew(() =>
                {
                    while (Occurrences > 0) ;
                });
                t.Wait(timeout);
            }

            return Occurrences == int.MaxValue || Occurrences == 0;
        }

        public T Intercept<T>(ActorSystem system, Func<T> func)
        {
            system.EventStream.Publish(new Mute(this));
            var testKitSettings = TestKitExtension.For(system);
            var leeway = testKitSettings.TestEventFilterLeeway;
            try
            {
                var result = func();

                if (!AwaitDone(leeway))
                {
                    var msg = Occurrences > 0
                        ? string.Format("Timeout ({0}) waiting for {1} messages on {2}", leeway, Occurrences, this)
                        : string.Format("Received {0} messages too many on {1}", -Occurrences, this);

                    var testKitAssertionsProvider = TestKitAssertionsExtension.For(system);
                    testKitAssertionsProvider.Assertions.Fail(msg);
                }
                return result;
            }
            finally
            {
                system.EventStream.Publish(new Unmute(this));
            }
        }

        protected bool DoMatch(string src, object msg)
        {
            var msgstr = msg == null ? "null" : msg.ToString();
            return (Source == src && !string.IsNullOrEmpty(Source) || string.IsNullOrEmpty(Source))
                   && (Complete ? msgstr == Message : msgstr.Contains(Message));
        }
    }

    /// <summary>
    /// Implementation to <see cref="EventFilter"/> facitilites. 
    /// To install a filter use Mute, and to uninstall - Unmute.
    /// </summary>
    public abstract class TestEvent
    {
    }

    public sealed class Mute : TestEvent, NoSerializationVerificationNeeded
    {
        public Mute(params EventFilter[] filters)
        {
            Filters = filters;
        }

        public EventFilter[] Filters { get; private set; }
    }

    public sealed class Unmute : TestEvent, NoSerializationVerificationNeeded
    {
        public Unmute(params EventFilter[] filters)
        {
            Filters = filters;
        }

        public EventFilter[] Filters { get; private set; }
    }
    
    public class ErrorFilter<TError> : EventFilter where TError: Exception
    {
        public ErrorFilter() 
            : this(int.MaxValue, null, null, false)
        {
        }

        public ErrorFilter(int occurrences, string message, string source, bool complete)
            : base(occurrences, message, source, complete)
        {
        }

        protected override bool IsMatch(LogEvent evt)
        {
            if (evt is Error)
            {
                var err = evt as Error;
                if (err.Cause is TError)
                {
                    return (err.Message == null && string.IsNullOrEmpty(err.Cause.Message) && string.IsNullOrEmpty(err.Cause.StackTrace))
                        || DoMatch(err.LogSource, err.Message)
                        || DoMatch(err.LogSource, err.Cause.Message);
                }
            }

            return false;
        }
    }

    public class WarningFilter : EventFilter
    {
        public WarningFilter() 
            : this(int.MaxValue, null, null, false)
        {
        }

        public WarningFilter(int occurrences, string message, string source, bool complete)
            : base(occurrences, message, source, complete)
        {
        }
        protected override bool IsMatch(LogEvent evt)
        {
            if (evt is Warning)
            {
                var warn = evt as Warning;
                return DoMatch(warn.LogSource, warn.Message);
            }

            return false;
        }
    }

    public class InfoFilter : EventFilter
    {
        public InfoFilter()
            : this(int.MaxValue, null, null, false)
        {
        }

        public InfoFilter(int occurrences, string message, string source, bool complete)
            : base(occurrences, message, source, complete)
        {
        }
        protected override bool IsMatch(LogEvent evt)
        {
            if (evt is Info)
            {
                var info = evt as Info;
                return DoMatch(info.LogSource, info.Message);
            }

            return false;
        }
    }

    public class DebugFilter : EventFilter
    {
        public DebugFilter()
            : this(int.MaxValue, null, null, false)
        {
        }

        public DebugFilter(int occurrences, string message, string source, bool complete)
            : base(occurrences, message, source, complete)
        {
        }
        protected override bool IsMatch(LogEvent evt)
        {
            if (evt is Debug)
            {
                var dbg = evt as Debug;
                return DoMatch(dbg.LogSource, dbg.Message);
            }

            return false;
        }
    }

    public class CustomEventFilter : EventFilter
    {
        private readonly Predicate<LogEvent> _predicate;

        public CustomEventFilter(int occurrences, Predicate<LogEvent> predicate) : base(occurrences)
        {
            _predicate = predicate;
        }

        protected override bool IsMatch(LogEvent evt)
        {
            return _predicate(evt);
        }
    }
}