// -----------------------------------------------------------------------
//  <copyright file="ContextWatchWithSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit;
using FluentAssertions.Extensions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Tests.Actor;

public class ContextWatchWithSpec : AkkaSpec
{
    private readonly ITestOutputHelper _outputHelper;

    public ContextWatchWithSpec(ITestOutputHelper outputHelper)
    {
        _outputHelper = outputHelper;
    }

    [Fact(Skip = "This test is used with Performance Profiler to check memory leaks")]
    public async Task Context_WatchWith_Should_not_have_memory_leak()
    {
        using (var actorSystem = ActorSystem.Create("repro"))
        {
            actorSystem.ActorOf(Props.Create<LoadHandler>());

            await Task.Delay(60.Seconds());
        }
    }

    public class LoadHandler : ReceiveActor
    {
        private readonly ICancelable _cancel;
        private readonly List<IActorRef> _subjects;

        public LoadHandler()
        {
            _subjects = new List<IActorRef>();
            _cancel = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(1),
                Self,
                Iteration.Instance,
                ActorRefs.NoSender);

            Receive<Iteration>(
                _ =>
                {
                    // stop actors created on previous iteration
                    _subjects.ForEach(Context.Stop);
                    _subjects.Clear();

                    // create a set of actors and start watching them
                    for (var i = 0; i < 10_000; i++)
                    {
                        var subject = Context.ActorOf(Props.Create<Subject>());
                        _subjects.Add(subject);
                        Context.WatchWith(subject, new Stopped(subject));
                    }
                });

            Receive<Stopped>(_ => { });
        }

        protected override void PostStop()
        {
            _cancel.Cancel();
        }

        private class Iteration
        {
            public static readonly Iteration Instance = new();

            private Iteration()
            {
            }
        }

        public class Stopped
        {
            public Stopped(IActorRef actorRef)
            {
                ActorRef = actorRef;
            }

            public IActorRef ActorRef { get; }
        }

        public class Subject : ReceiveActor
        {
            // simulate internal state
            private byte[] _state = new byte[1000];
        }
    }
}