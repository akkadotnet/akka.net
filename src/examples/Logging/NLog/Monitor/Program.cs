using Akka.Actor;
using Akka.Configuration.Hocon;
using Monitor.Actors;
using Monitor.Actors.Events;
using Monitor.Extensions;
using System;
using System.Configuration;

namespace Monitor
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            var cfg = (AkkaConfigurationSection)ConfigurationManager.GetSection("akka");
            using (var system = ActorSystem.Create("myVerySimpleMonitoringSystem", cfg.AkkaConfig))
            {
                var supervisor = system.ActorOf<MonitorSupervisor>("supervisor");

                supervisor.Tell(new SpawnMonitor("akkaWebSiteMonitor", "http://akkadotnet.github.io", 10.Seconds(), 10.Seconds()));
                supervisor.Tell(new SpawnMonitor("googleWebSiteMonitor", "http://www.google.com", 2.Seconds(), 5.Seconds()));

                Console.ReadLine();
                Console.WriteLine("Shutting down...");
            }
        }
    }
}