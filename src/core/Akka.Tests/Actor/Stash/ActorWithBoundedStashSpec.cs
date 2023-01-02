﻿//-----------------------------------------------------------------------
// <copyright file="ActorWithBoundedStashSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Event;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Tests.Actor.Stash
{
    public class StashingActor : UntypedActor, IWithBoundedStash
    {
        public IStash Stash { get; set; }

        protected override void OnReceive(object message)
        {
            if (message is string s && s.StartsWith("hello"))
            {
                Stash.Stash();
                Sender.Tell("ok");
            }
            else if (message.Equals("world"))
            {
                Context.Become(AfterWorldBehavior);
                Stash.UnstashAll();
            }
        }

        private void AfterWorldBehavior(object message) => Stash.Stash();
    }

    public class StashingActorWithOverflow : UntypedActor, IWithBoundedStash
    {
        private int numStashed = 0;

        public IStash Stash { get; set; }

        protected override void OnReceive(object message)
        {
            if (!(message is string s) || !s.StartsWith("hello"))
                return;

            numStashed++;
            try
            {
                Stash.Stash();
                Sender.Tell("ok");
            }
            catch (Exception ex) when (ex is StashOverflowException)
            {
                if (numStashed == 21)
                {
                    Sender.Tell("STASHOVERFLOW");
                    Context.Stop(Self);
                }
                else
                {
                    Sender.Tell("Unexpected StashOverflowException: " + numStashed);
                }
            }
        }
    }

    // bounded deque-based mailbox with capacity 10
    public class Bounded10 : BoundedDequeBasedMailbox
    {
        public Bounded10(Settings settings, Config config)
            : base(settings, config)
        { }
    }

    public class Bounded100 : BoundedDequeBasedMailbox
    {
        public Bounded100(Settings settings, Config config)
            : base(settings, config)
        { }
    }

    public class ActorWithBoundedStashSpec : AkkaSpec
    {
        private static Config SpecConfig => ConfigurationFactory.ParseString(@$"
            akka.loggers = [""Akka.TestKit.TestEventListener, Akka.TestKit""]
            my-dispatcher-1 {{
                mailbox-type = ""{typeof(Bounded10).AssemblyQualifiedName}""
                mailbox-capacity = 10
                mailbox-push-timeout-time = 500ms
                stash-capacity = 20
            }}
            my-dispatcher-2 {{
                mailbox-type = ""{typeof(Bounded100).AssemblyQualifiedName}""
                mailbox-capacity = 100
                mailbox-push-timeout-time = 500ms
                stash-capacity = 20
            }}
            my-aliased-dispatcher-1 = my-dispatcher-1
            my-aliased-dispatcher-2 = my-aliased-dispatcher-1
            my-mailbox-1 {{
                mailbox-type = ""{typeof(Bounded10).AssemblyQualifiedName}""
                mailbox-capacity = 10
                mailbox-push-timeout-time = 500ms
                stash-capacity = 20
            }}
            my-mailbox-2 {{
                mailbox-type = ""{typeof(Bounded100).AssemblyQualifiedName}""
                mailbox-capacity = 100
                mailbox-push-timeout-time = 500ms
                stash-capacity = 20
            }}");

        public ActorWithBoundedStashSpec(ITestOutputHelper outputHelper)
            : base(outputHelper, SpecConfig)
        {
            Sys.EventStream.Subscribe(TestActor, typeof(DeadLetter));
        }

        protected override void AtStartup()
        {
            base.AtStartup();
            Sys.EventStream.Publish(EventFilter.Warning(pattern: new Regex(".*Received dead letter from.*hello.*")).Mute());
        }

        protected override async Task AfterAllAsync()
        {
            await base.AfterAllAsync();
            Sys.EventStream.Unsubscribe(TestActor, typeof(DeadLetter));
        }

        private void TestDeadLetters(IActorRef stasher)
        {
            for (var n = 1; n <= 11; n++)
            {
                stasher.Tell("hello" + n);
                ExpectMsg("ok");
            }

            // cause unstashAll with capacity violation
            stasher.Tell("world");
            ExpectMsg<DeadLetter>().Equals(new DeadLetter("hello1", TestActor, stasher));

            AwaitCondition(() => ExpectMsg<DeadLetter>() != null);

            stasher.Tell(PoisonPill.Instance);
            // stashed messages are sent to deadletters when stasher is stopped
            for (var n = 2; n <= 11; n++)
                ExpectMsg<DeadLetter>().Equals(new DeadLetter("hello" + n, TestActor, stasher));
        }

        private void TestStashOverflowException(IActorRef stasher)
        {
            // fill up stash
            for (var n = 1; n <= 20; n++)
            {
                stasher.Tell("hello" + n);
                ExpectMsg("ok");
            }

            stasher.Tell("hello21");
            ExpectMsg("STASHOVERFLOW");

            // stashed messages are sent to deadletters when stasher is stopped
            for (var n = 1; n <= 20; n++)
                ExpectMsg<DeadLetter>().Equals(new DeadLetter("hello" + n, TestActor, stasher));
        }

        [Fact(Skip = "DequeWrapperMessageQueue implementations are not actually double-ended queues, so this cannot currently work.")]
        public void An_actor_with_stash_must_end_up_in_DeadLetters_in_case_of_a_capacity_violation_when_configure_via_dispatcher()
        {
            var stasher = Sys.ActorOf(Props.Create<StashingActor>().WithDispatcher("my-dispatcher-1"));
            TestDeadLetters(stasher);
        }

        [Fact(Skip = "DequeWrapperMessageQueue implementations are not actually double-ended queues, so this cannot currently work.")]
        public void An_actor_with_stash_must_end_up_in_DeadLetters_in_case_of_a_capacity_violation_when_configure_via_mailbox()
        {
            var stasher = Sys.ActorOf(Props.Create<StashingActor>().WithDispatcher("my-mailbox-1"));
            TestDeadLetters(stasher);
        }

        [Fact]
        public void An_actor_with_stash_must_throw_a_StashOverflowException_in_case_of_a_stash_capacity_violation_when_configured_via_dispatcher()
        {
            var stasher = Sys.ActorOf(Props.Create<StashingActorWithOverflow>().WithDispatcher("my-dispatcher-2"));
            TestStashOverflowException(stasher);
        }

        [Fact]
        public void An_actor_with_stash_must_throw_a_StashOverflowException_in_case_of_a_stash_capacity_violation_when_configured_via_mailbox()
        {
            var stasher = Sys.ActorOf(Props.Create<StashingActorWithOverflow>().WithDispatcher("my-mailbox-2"));
            TestStashOverflowException(stasher);
        }

        [Fact]
        public void An_actor_with_stash_must_get_stash_capacity_from_aliased_dispatchers()
        {
            var stasher = Sys.ActorOf(Props.Create<StashingActorWithOverflow>().WithDispatcher("my-aliased-dispatcher-2"));
            TestStashOverflowException(stasher);
        }
    }
}
