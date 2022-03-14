//-----------------------------------------------------------------------
// <copyright file="Bug4762FixSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Xunit;

namespace Akka.Persistence.TestKit.Tests
{
    /// <summary>
    /// Fix spec for bug https://github.com/akkadotnet/akka.net/issues/4762
    /// </summary>
    public class Bug4762FixSpec : PersistenceTestKit
    {
        class WriteMessage
        { }

        class TestEvent
        { }

        class TestActor2 : UntypedPersistentActor
        {
            private readonly IActorRef _probe;
            private readonly ILoggingAdapter _log;
            public override string PersistenceId => "foo";

            public TestActor2(IActorRef probe)
            {
                _log = Context.GetLogger();
                _probe = probe;
            }

            protected override void OnCommand(object message)
            {
                switch (message)
                {
                    case WriteMessage _:
                        var event1 = new TestEvent();
                        var event2 = new TestEvent();
                        var events = new List<TestEvent> { event1, event2 };
                        PersistAll(events, _ =>
                        {
                            _probe.Tell(Done.Instance);
                        });
                        break;

                    default:
                        return;
                }
            }

            protected override void OnRecover(object message)
            {
                _probe.Tell(message);
            }
        }

        [Fact]
        public async Task TestJournal_PersistAll_should_only_count_each_event_exceptions_once()
        {
            var probe = CreateTestProbe();
            await WithJournalWrite(write => write.Pass(), () =>
            {
                var actor = ActorOf(() => new TestActor2(probe));
                Watch(actor);

                var command = new WriteMessage();
                actor.Tell(command, actor);

                probe.ExpectMsg<RecoveryCompleted>();
                probe.ExpectMsg<Done>();
                probe.ExpectMsg<Done>();
                probe.ExpectNoMsg(3000);
            });
        }
    }
}
