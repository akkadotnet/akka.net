//-----------------------------------------------------------------------
// <copyright file="BugFix6948Spec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.TestKit;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Tests.Actor;

public class BugFix6948Spec : AkkaSpec
{
    public BugFix6948Spec(ITestOutputHelper output): base("akka.loglevel=DEBUG", output)
    {
    }

    [Fact(DisplayName = "#6948 ActorSystem should terminate cleanly on fatal exceptions")]
    public async Task ActorSystem_ShouldTerminateCleanlyOnOOM()
    {
        var actor = Sys.ActorOf(Props.Create(() => new OutOfMemoryActor()));
        actor.Tell("die");
        ExpectMsg("dead");
        
        var shutdown = CoordinatedShutdown.Get(Sys);
        shutdown.AddTask(CoordinatedShutdown.PhaseBeforeActorSystemTerminate, "z", () =>
        {
            var pressure = new List<long>();
            try
            {
                foreach (var i in Enumerable.Range(0, 50_000_000))
                {
                    pressure.Add(i);
                }
            }
            catch (OutOfMemoryException ex)
            {
                Log.Error(ex, "I died (CoordinatedShutdown)");
                throw;
            }

            return Task.FromResult(Done.Instance);
        });

        var terminated = false;
        Sys.RegisterOnTermination(() =>
        {
            terminated = true;
        });
        
        await shutdown.Run(CoordinatedShutdown.UnknownReason.Instance);
        await Sys.Terminate().WaitAsync(5.Seconds());
        terminated.Should().BeTrue();
    }

    private class OutOfMemoryActor : ReceiveActor
    {
        private readonly List<long> _memoryPressure = new ();
        
        public OutOfMemoryActor()
        {
            var log = Context.GetLogger();
            
            Receive<string>(_ =>
            {
                try
                {
                    foreach (var i in Enumerable.Range(0, 50_000_000))
                    {
                        _memoryPressure.Add(i);
                    }
                }
                catch (OutOfMemoryException e)
                {
                    log.Error(e, "I died (Actor)");
                    Sender.Tell("dead");
                }
            });
        }
    }
}