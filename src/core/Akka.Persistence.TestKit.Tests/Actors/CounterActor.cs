//-----------------------------------------------------------------------
// <copyright file="CounterActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Persistence.TestKit.Tests
{
    using System;
    using System.Threading.Tasks;
    using Actor;
    using Akka.TestKit;
    using Xunit;

    public class CounterActor : UntypedPersistentActor
    {
        public CounterActor(string id)
        {
            this.PersistenceId = id;
        }

        private int _value = 0;

        public override string PersistenceId { get; }

        protected override void OnCommand(object message)
        {
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
        [Fact]
        public async Task CounterActor_internal_state_will_be_lost_if_underlying_persistence_store_is_not_available()
        {
            await WithJournalWrite(write => write.Fail(), async () => 
            {
                var counterProps = Props.Create(() => new CounterActor("test"));
                var actor = ActorOf(counterProps, "counter");
                
                Watch(actor);
                actor.Tell("inc", TestActor);
                ExpectMsg<Terminated>(TimeSpan.FromSeconds(3));

                // need to restart actor
                actor = ActorOf(counterProps, "counter1");
                actor.Tell("read", TestActor);

                var value = ExpectMsg<int>(TimeSpan.FromSeconds(3));
                value.ShouldBe(0);
            });
        }
    }
}
