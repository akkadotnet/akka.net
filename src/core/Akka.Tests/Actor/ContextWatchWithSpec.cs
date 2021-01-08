//-----------------------------------------------------------------------
// <copyright file="ContextWatchWithSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit;
using FluentAssertions;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Tests.Actor
{
    public class ContextWatchWithSpec : AkkaSpec
    {
        private readonly ITestOutputHelper _outputHelper;

        public ContextWatchWithSpec(ITestOutputHelper outputHelper)
        {
            _outputHelper = outputHelper;
        }
        
        [Fact(Skip = "This test is used with Performance Profiler to check memory leaks")]
        public void Context_WatchWith_Should_not_have_memory_leak()
        {
            using (var actorSystem = ActorSystem.Create("repro"))
            {
                actorSystem.ActorOf(Props.Create<LoadHandler>());

                Thread.Sleep(60.Seconds());
            }
        }
        
        public class LoadHandler : ReceiveActor
        {
            private readonly List<IActorRef> _subjects;
            private readonly ICancelable _cancel;

            public LoadHandler()
            {
                _subjects = new List<IActorRef>();
                _cancel = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(
                    initialDelay: TimeSpan.FromSeconds(1),
                    interval: TimeSpan.FromSeconds(1),
                    receiver: Self,
                    message: Iteration.Instance,
                    sender: ActorRefs.NoSender);

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

            private class Iteration
            {
                public static readonly Iteration Instance = new Iteration();
                private Iteration() { }
            }

            public class Stopped
            {
                public IActorRef ActorRef { get; }

                public Stopped(IActorRef actorRef)
                {
                    ActorRef = actorRef;
                }
            }

            public class Subject : ReceiveActor
            {
                // simulate internal state
                private byte[] _state = new byte[1000];
            }

            protected override void PostStop() => _cancel.Cancel();
        }
    }
}
