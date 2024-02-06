//-----------------------------------------------------------------------
// <copyright file="CounterActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Configuration;
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

        protected override void PostStop()
        {
            _log.Info("Shutting down");
        }
        
        protected override void PreStart()
        {
            _log.Info("Starting up");
        }
    }

    public class CounterActorTests : PersistenceTestKit
    {
        // create a Config that enables debug mode on the TestJournal
        private static readonly Config Config =
            ConfigurationFactory.ParseString("""
                                             akka.persistence.journal.test.debug = on
                                             akka.persistence.snapshot-store.test.debug = on
                                             """);
        
        public CounterActorTests(ITestOutputHelper output) : base(Config, output:output){}
        
        [Fact]
        public Task CounterActor_internal_state_will_be_lost_if_underlying_persistence_store_is_not_available()
        {
            return WithJournalWrite(write => write.Fail(), async () => 
            {
                var counterProps = Props.Create(() => new CounterActor("test"))
                    .WithDispatcher("akka.actor.internal-dispatcher");
                var actor = ActorOf(counterProps, "counter");
                
                Sys.Log.Info("Messaging actor");
                await WatchAsync(actor);
                actor.Tell("inc", TestActor);
                await ExpectTerminatedAsync(actor);

                // need to restart actor
                actor = ActorOf(counterProps, "counter1");
                actor.Tell("read", TestActor);

                var value = await ExpectMsgAsync<int>(TimeSpan.FromSeconds(3));
                value.ShouldBe(0);
            });
        }
    }
}
