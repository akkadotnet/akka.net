using System;

namespace Monitor.Actors.Events
{
    /// <summary>
    /// Contains information about a results of a recent monitoring check.
    /// This is sent from a Monitor to a MonitorResultProcessor which processes
    /// the results
    /// </summary>
    public class MonitoringResult
    {
        public MonitoringResult(Uri uri, TimeSpan time)
            : this(uri, null, time)
        {
        }

        public MonitoringResult(Uri uri, Exception exception, TimeSpan time)
        {
            Uri = uri;
            Error = exception != null;
            Time = time;
            Exception = exception;
        }

        public Uri Uri { get; private set; }

        public bool Error { get; private set; }

        public Exception Exception { get; private set; }

        public TimeSpan Time { get; private set; }
    }
}