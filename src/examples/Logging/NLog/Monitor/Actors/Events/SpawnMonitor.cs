using System;

namespace Monitor.Actors.Events
{
    /// <summary>
    /// Tells the MonitorSupervisor to spawn a child actor /user/monitor/{Name} and
    /// schedules the RunMonitoringCheck at the given interval
    /// </summary>
    public class SpawnMonitor
    {
        public SpawnMonitor(string name, string url, TimeSpan interval, TimeSpan timeout)
        {
            Interval = interval;
            Name = name;
            Url = url;
            TimeOut = timeout;
        }

        public TimeSpan Interval { get; private set; }

        public string Name { get; private set; }

        public string Url { get; private set; }

        public TimeSpan TimeOut { get; private set; }
    }
}