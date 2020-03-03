//-----------------------------------------------------------------------
// <copyright file="ActorHierarchyExperiments.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.TestKit.Xunit2;
using Xunit;

namespace Tutorials.Tutorial1
{
    #region print-refs
    public class PrintMyActorRefActor : UntypedActor
    {
        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case "printit":
                    IActorRef secondRef = Context.ActorOf(Props.Empty, "second-actor");
                    Console.WriteLine($"Second: {secondRef}");
                    break;
            }
        }
    }
    #endregion

    #region start-stop
    public class StartStopActor1 : UntypedActor
    {
        protected override void PreStart()
        {
            Console.WriteLine("first started");
            Context.ActorOf(Props.Create<StartStopActor2>(), "second");
        }

        protected override void PostStop() => Console.WriteLine("first stopped");

        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case "stop":
                    Context.Stop(Self);
                    break;
            }
        }
    }

    public class StartStopActor2 : UntypedActor
    {
        protected override void PreStart() => Console.WriteLine("second started");
        protected override void PostStop() => Console.WriteLine("second stopped");

        protected override void OnReceive(object message)
        {
        }
    }
    #endregion

    #region supervise
    public class SupervisingActor : UntypedActor
    {
        private IActorRef child = Context.ActorOf(Props.Create<SupervisedActor>(), "supervised-actor");

        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case "failChild":
                    child.Tell("fail");
                    break;
            }
        }
    }

    public class SupervisedActor : UntypedActor
    {
        protected override void PreStart() => Console.WriteLine("supervised actor started");
        protected override void PostStop() => Console.WriteLine("supervised actor stopped");

        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case "fail":
                    Console.WriteLine("supervised actor fails now");
                    throw new Exception("I failed!");
            }
        }
    }
    #endregion

    public class ActorHierarchyExperiments : TestKit
    {
        [Fact]
        public void Create_top_and_child_actor()
        {
            #region print-refs2
            var firstRef = Sys.ActorOf(Props.Create<PrintMyActorRefActor>(), "first-actor");
            Console.WriteLine($"First: {firstRef}");
            firstRef.Tell("printit", ActorRefs.NoSender);
            #endregion
        }

        [Fact]
        public void Start_and_stop_actors()
        {
            #region start-stop2
            var first = Sys.ActorOf(Props.Create<StartStopActor1>(), "first");
            first.Tell("stop");
            #endregion
        }

        [Fact]
        public void Supervise_actors()
        {
            #region supervise2
            var supervisingActor = Sys.ActorOf(Props.Create<SupervisingActor>(), "supervising-actor");
            supervisingActor.Tell("failChild");
            #endregion
        }
    }
}
