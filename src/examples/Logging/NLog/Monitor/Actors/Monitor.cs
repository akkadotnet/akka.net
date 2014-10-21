using Akka.Actor;
using Akka.Event;
using Monitor.Actors.Events;
using Monitor.Exceptions;
using Monitor.Utils;
using System;
using System.Net;
using System.Threading;

namespace Monitor.Actors
{
    /// <summary>
    /// The Monitor actor is our workhorse. It is triggered by a RunMonitoringCheck message to perform a monitoring check on a
    /// pre-set website and see if it responds within a pre-set timeout. Each time a check is triggered, the actor performs a
    /// D6 dice roll (D6 = 1..6). On 1..3 - the monitor performs the check, on 4-5 a non-fatal exception is thrown. On 6 - a fatal
    /// exception is thrown, signalling the suprvisor to restart this actor because it's now "dead".
    /// </summary>
    public class Monitor : TypedActor, IHandle<RunMonitoringCheck>
    {
        private readonly LoggingAdapter _logger = Logging.GetLogger(Context);
        private readonly Uri _uri;
        private readonly TimeSpan _timeout;

        public Monitor(string url, TimeSpan timeout)
        {
            _uri = new Uri(url);
            _timeout = timeout;
        }

        public void Handle(RunMonitoringCheck message)
        {
            _logger.Debug("{0} starting a monitoring check on {1}", Context.Self.Path.Name, _uri);
            using (var dice = new Dice(PerformMonitoringCheck))
            {
                dice
                    .On(4, () => { throw new NonFatalErrorException(); })
                    .On(5, () => { throw new NonFatalErrorException(); })
                    .On(6, () => { throw new FatalErrorException(); })
                    .Roll();
            }
        }

        private void PerformMonitoringCheck()
        {
            using (var scope = MeasurementScope.Execute((uri, timeout) =>
            {
                // Additional headers required so that the client doesn't timeout
                using (var client = new UrlMonitoringClient())
                {
                    client.HeadOnly = false;
                    client.TimeOut = Convert.ToInt32(timeout.TotalMilliseconds);
                    client.Headers[HttpRequestHeader.Accept] = "text/html, image/png, image/jpeg, image/gif, */*;q=0.1";
                    client.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0 (Windows; U; Windows NT 6.1; de; rv:1.9.2.12) Gecko/20101026 Firefox/3.6.12";
                    var x = client.DownloadString(uri);
                }
            })
                .OnFinish((result =>
                {
                    var formattingString = !result.Error
                        ? "{0} GET {1} completed on thread [{2}] in {3} [ms]"
                        : "{0} GET {1} completed with errors on thread [{2}] in {3} [ms]";

                    if (!result.Error)
                    {
                        _logger.Debug(formattingString, _uri.Scheme.ToUpper(), _uri, Thread.CurrentThread.ManagedThreadId,
                            result.Time.TotalMilliseconds);
                    }
                    else
                    {
                        _logger.Warn(formattingString, _uri.Scheme.ToUpper(), _uri, Thread.CurrentThread.ManagedThreadId,
                            result.Time.TotalMilliseconds);
                    }

                    var processor = Context.ActorSelection("../processor");
                    processor.Tell(result);
                })))
            {
                scope.Run<WebException>(_uri, _timeout);
            }
        }

        /// <summary>
        /// UrlMonitoring client is a modified WebClient, capable of retrieving HEAD only and supporting timeouts.
        /// </summary>
        private class UrlMonitoringClient : WebClient
        {
            public bool HeadOnly { get; set; }

            public int TimeOut
            {
                get;
                set;
            }

            protected override WebRequest GetWebRequest(Uri address)
            {
                var req = base.GetWebRequest(address);

                if (HeadOnly && req.Method.Equals("GET"))
                {
                    req.Method = "HEAD";
                }

                req.Timeout = TimeOut;

                return req;
            }
        }
    }
}