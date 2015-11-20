//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Threading;
using Akka.Actor;
using Helios.Concurrency;

namespace TimeClient
{
    internal class Program
    {
        static bool IsShutdown = false;

        private static void Main(string[] args)
        {
            using (var system = ActorSystem.Create("TimeClient"))
            {
                var tmp = system.ActorSelection("akka.tcp://TimeServer@localhost:9391/user/time");
                Console.Title = string.Format("TimeClient {0}", Process.GetCurrentProcess().Id);
                var timeClient = system.ActorOf(Props.Create(() => new TimeClientActor(tmp)), "timeChecker");


                var fiber = FiberFactory.CreateFiber(3);

                while (!Program.IsShutdown)
                {
                    timeClient.Tell(Time);
                }

                Console.WriteLine("Connection closed.");

                Console.ReadLine();
                IsShutdown = true;
                Console.WriteLine("Shutting down...");
                Console.WriteLine("Terminated");
            }
        }

        public static CheckTime Time = new CheckTime();
        public class CheckTime { }

        public class TimeClientActor : TypedActor, IHandle<string>, IHandle<CheckTime>
        {
            private readonly ICanTell _timeServer;

            public TimeClientActor(ICanTell timeServer)
            {
                _timeServer = timeServer;
            }

            public void Handle(string message)
            {
                Console.WriteLine(message);
            }

            public void Handle(CheckTime message)
            {
                _timeServer.Tell("gettime", Self);
            }
        }
    }
}

