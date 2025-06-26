//-----------------------------------------------------------------------
// <copyright file="CounterActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Event;
using Xunit.Abstractions;

namespace Akka.Persistence.TestKit.Tests
{
    using System;
    using System.Threading.Tasks;
    using Actor;
    using Akka.TestKit;
    using Xunit;

    public class CounterActor : UntypedPersistentActor
    {
        private readonly ILoggingAdapter _log = Context.GetLogger();
        
        public CounterActor(string id)
        {
            PersistenceId = id;
        }

        private int _value = 0;

        public override string PersistenceId { get; }

        protected override void OnCommand(object message)
        {
            _log.Info("Received command {0}", message);
            
            switch (message as string)
            {
                case "inc":
                    _value++;
                    Persist(message, _ => { });
                    break;

                case "dec":
                    _value++;
                    Persist(message, _ => { });
                    break;

                case "read":
                    Sender.Tell(_value, Self);
                    break;

                default:
                    return;
            }
        }

        protected override void OnRecover(object message)
        {
            _log.Info("Received recover {0}", message);
            
            switch (message as string)
            {
                case "inc":
                    _value++;
                    break;

                case "dec":
                    _value++;
                    break;
        
                default:
                    return;
            }
        }
    }

    public class CounterActorTests : PersistenceTestKit
    {
        public CounterActorTests(ITestOutputHelper output) : base(output:output){}
        
        [Fact]
        public Task CounterActor_internal_state_will_be_lost_if_underlying_persistence_store_is_not_available()
        {
            return WithJournalWrite(write => write.Fail(), async () => 
            {
                var counterProps = Props.Create(() => new CounterActor("test"));
                var actor = ActorOf(counterProps, "counter");
                
                Watch(actor);
                actor.Tell("inc", TestActor);
                await ExpectMsgAsync<Terminated>(TimeSpan.FromSeconds(3));

                // need to restart actor
                actor = ActorOf(counterProps, "counter1");
                actor.Tell("read", TestActor);

                var value = await ExpectMsgAsync<int>(TimeSpan.FromSeconds(3));
                value.ShouldBe(0);
            });
        }
    }
}
