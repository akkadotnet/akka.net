//-----------------------------------------------------------------------
// <copyright file="MailboxesSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Dispatch.MessageQueues;
using Akka.Dispatch.SysMsg;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Akka.Tests.Actor;
using Akka.Util.Internal;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using Xunit;
using Config = Akka.Configuration.Config;

namespace Akka.Tests.Dispatch
{
    public class TestPriorityMailbox : UnboundedPriorityMailbox
    {
        protected override int PriorityGenerator(object message)
        {
            if (message is string)
                return 1;
            if (message is bool)
                return 2;
            if (message is int)
                return 3;
            if (message is double)
                return 4;

            return 5;
        }

        public TestPriorityMailbox(Settings settings, Config config) : base(settings, config)
        {
        }
    }

    public class TestStablePriorityMailbox : UnboundedStablePriorityMailbox
    {
        protected override int PriorityGenerator(object message)
        {
            if (message is string)
                return 1;
            if (message is bool)
                return 2;
            if (message is int)
                return 3;
            if (message is double)
                return 4;

            return 5;
        }

        public TestStablePriorityMailbox(Settings settings, Config config) : base(settings, config)
        {
        }
    }

    public class IntPriorityMailbox : UnboundedPriorityMailbox
    {
        protected override int PriorityGenerator(object message)
        {
            return message as int? ?? Int32.MaxValue;
        }

        public IntPriorityMailbox(Settings settings, Config config) : base(settings, config)
        {
        }
    }

    public class StashingActor : ReceiveActor, IWithUnboundedStash
    {
        private readonly TestKitBase _testkit;
        private readonly bool _echoBackToSenderAsWell;
        public IStash Stash { get; set; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="testkit">TBD</param>
        /// <param name="echoBackToSenderAsWell">TBD</param>
        public StashingActor(TestKitBase testkit, bool echoBackToSenderAsWell = true)
        {
            _testkit = testkit;
            _echoBackToSenderAsWell = echoBackToSenderAsWell;
            StashAll();
        }

        private void StashAll()
        {
            Receive<Start>(_ =>
            {
                Become(Process);
                Stash.UnstashAll();
            });
            ReceiveAny(msg =>
            {
                Stash.Stash();
            });
        }

        private void Process()
        {
            ReceiveAny(msg =>
            {
                var sender = Sender;
                var testActor = _testkit.TestActor;
                if (_echoBackToSenderAsWell && testActor != sender)
                    sender.Forward(msg);
                testActor.Tell(msg, Sender);
            });
        }

        /// <summary>
        /// Returns a <see cref="Props"/> object that can be used to create an <see cref="EchoActor"/>.
        /// The  <see cref="EchoActor"/> echoes whatever is sent to it, to the
        /// TestKit's <see cref="TestKitBase.TestActor"/>.
        /// By default it also echoes back to the sender, unless the sender is the <see cref="TestKitBase.TestActor"/>
        /// (in this case the <see cref="TestKitBase.TestActor"/> will only receive one message) or unless 
        /// <paramref name="echoBackToSenderAsWell"/> has been set to <c>false</c>.
        /// </summary>
        /// <param name="testkit">TBD</param>
        /// <param name="echoBackToSenderAsWell">TBD</param>
        /// <returns>TBD</returns>
        public static Props Props(TestKitBase testkit, bool echoBackToSenderAsWell = true)
        {
            return Akka.Actor.Props.Create(() => new StashingActor(testkit, echoBackToSenderAsWell));
        }

        public class Start { }
    }

    public class MailboxesSpec : AkkaSpec
    {
        public MailboxesSpec() : base(GetConfig())
        {            
        }

        private static string GetConfig()
        {
               return @"
akka.actor.default-dispatcher.throughput = 100  #ensure we process 100 messages per mailbox run
string-prio-mailbox {
    mailbox-type : """ + typeof(TestPriorityMailbox).AssemblyQualifiedName  + @"""
}

int-prio-mailbox {
    mailbox-type : """ + typeof(IntPriorityMailbox).AssemblyQualifiedName + @"""
}
stable-prio-mailbox{
    mailbox-type : """ + typeof(TestStablePriorityMailbox).AssemblyQualifiedName + @"""
}
";
        }

#if FSCHECK
        [Property]
        public Property UnboundedPriorityQueue_should_sort_items_in_expected_order(int[] integers, PositiveInt capacity)
        {
            var pq = new UnboundedPriorityMessageQueue(o => o as int? ?? int.MaxValue, capacity.Get);
            var expectedOrder = integers.OrderBy(x => x).ToList();
            var actualOrder = new List<int>(integers.Length);

            // build up the entire list
            var loop = Parallel.ForEach(integers, i =>
            {
                pq.Enqueue(ActorRefs.Nobody, new Envelope(i, ActorRefs.NoSender));
            });
            AwaitCondition(() => loop.IsCompleted);

            Envelope e;

            // now that everything is sorted, dequeue it into its expected order
            while (pq.TryDequeue(out e))
            {
                actualOrder.Add((int)e.Message);
            }

            return
                expectedOrder.SequenceEqual(actualOrder)
                    .Label($"Expected [{string.Join(";", expectedOrder)}], but was [{string.Join(";", actualOrder)}]");
        }
#endif

        [Fact]
        public void Can_use_unbounded_priority_mailbox()
        {
            var actor = (IInternalActorRef)Sys.ActorOf(EchoActor.Props(this).WithMailbox("string-prio-mailbox"), "echo");

            //pause mailbox until all messages have been told
            actor.SendSystemMessage(new Suspend());

            // wait until we can confirm that the mailbox is suspended before we begin sending messages
            AwaitCondition(() => (((ActorRefWithCell)actor).Underlying is ActorCell) && ((ActorRefWithCell)actor).Underlying.AsInstanceOf<ActorCell>().Mailbox.IsSuspended());

            actor.Tell(true);
            for (var i = 0; i < 30; i++)
            {
                actor.Tell(1);    
            }
            actor.Tell("a");
            actor.Tell(2.0);
            for (var i = 0; i < 30; i++)
            {
                actor.Tell(1);
            }
            actor.SendSystemMessage(new Resume(null));

            //resume mailbox, this prevents the mailbox from running to early
            //priority mailbox is best effort only
            
            ExpectMsg("a");
            ExpectMsg(true);
            for (var i = 0; i < 60; i++)
            {
                ExpectMsg(1);
            }
            ExpectMsg(2.0);

            ExpectNoMsg(TimeSpan.FromSeconds(0.3));
        }

        [Fact]
        public void Can_use_unbounded_stable_priority_mailbox()
        {
            var actor = (IInternalActorRef)Sys.ActorOf(EchoActor.Props(this).WithMailbox("stable-prio-mailbox"), "echo");

            //pause mailbox until all messages have been told
            actor.SendSystemMessage(new Suspend());

            // wait until we can confirm that the mailbox is suspended before we begin sending messages
            AwaitCondition(() => (((ActorRefWithCell)actor).Underlying is ActorCell) && ((ActorRefWithCell)actor).Underlying.AsInstanceOf<ActorCell>().Mailbox.IsSuspended());

            actor.Tell(true);
            for (var i = 0; i < 30; i++)
            {
                actor.Tell(i);
            }
            actor.Tell("a");
            actor.Tell(2.0);
            for (var i = 0; i < 30; i++)
            {
                actor.Tell(i + 30);
            }
            actor.SendSystemMessage(new Resume(null));

            //resume mailbox, this prevents the mailbox from running to early
            //priority mailbox is best effort only

            ExpectMsg("a");
            ExpectMsg(true);
            for (var i = 0; i < 60; i++)
            {
                ExpectMsg(i);
            }
            ExpectMsg(2.0);

            ExpectNoMsg(TimeSpan.FromSeconds(0.3));
        }

        [Fact]
        public void Priority_mailbox_keeps_ordering_with_many_priority_values()
        {
            var actor = (IInternalActorRef)Sys.ActorOf(EchoActor.Props(this).WithMailbox("int-prio-mailbox"), "echo");

            //pause mailbox until all messages have been told
            actor.SendSystemMessage(new Suspend());

            AwaitCondition(()=> (((ActorRefWithCell)actor).Underlying is ActorCell) && ((ActorRefWithCell)actor).Underlying.AsInstanceOf<ActorCell>().Mailbox.IsSuspended());
            // creates 50 messages with values spanning from Int32.MinValue to Int32.MaxValue
            var values = new int[50];
            var increment = (int)(UInt32.MaxValue / values.Length);

            for (var i = 0; i < values.Length; i++)
                values[i] = Int32.MinValue + increment * i;

            // tell the actor in reverse order
            foreach (var value in values.Reverse())
            {
                actor.Tell(value);
                actor.Tell(value);
                actor.Tell(value);
            }

            //resume mailbox, this prevents the mailbox from running to early
            actor.SendSystemMessage(new Resume(null));

            // expect the messages in the correct order
            foreach (var value in values)
            {
                ExpectMsg(value);
                ExpectMsg(value);
                ExpectMsg(value);
            }

            ExpectNoMsg(TimeSpan.FromSeconds(0.3));
        }

        [Fact]
        public void Unbounded_Priority_Mailbox_Supports_Unbounded_Stashing()
        {
            var actor = (IInternalActorRef)Sys.ActorOf(StashingActor.Props(this).WithMailbox("int-prio-mailbox"), "echo");

            //pause mailbox until all messages have been told
            actor.SendSystemMessage(new Suspend());

            AwaitCondition(() => (((ActorRefWithCell)actor).Underlying is ActorCell) && ((ActorRefWithCell)actor).Underlying.AsInstanceOf<ActorCell>().Mailbox.IsSuspended());

            var values = new int[10];
            var increment = (int)(UInt32.MaxValue / values.Length);

            for (var i = 0; i < values.Length; i++)
                values[i] = Int32.MinValue + increment * i;

            // tell the actor in reverse order
            foreach (var value in values.Reverse())
            {
                actor.Tell(value);
                actor.Tell(value);
                actor.Tell(value);
            }

            actor.Tell(new StashingActor.Start());

            //resume mailbox, this prevents the mailbox from running to early
            actor.SendSystemMessage(new Resume(null));

            this.Within(5.Seconds(), () =>
            {
                // expect the messages in the correct order
                foreach (var value in values)
                {
                    ExpectMsg(value);
                    ExpectMsg(value);
                    ExpectMsg(value);
                }
            }); 

            ExpectNoMsg(TimeSpan.FromSeconds(0.3));
        }

        [Fact]
        public void Unbounded_Stable_Priority_Mailbox_Supports_Unbounded_Stashing()
        {
            var actor = (IInternalActorRef)Sys.ActorOf(StashingActor.Props(this).WithMailbox("stable-prio-mailbox"), "echo");

            //pause mailbox until all messages have been told
            actor.SendSystemMessage(new Suspend());

            AwaitCondition(() => (((ActorRefWithCell)actor).Underlying is ActorCell) && ((ActorRefWithCell)actor).Underlying.AsInstanceOf<ActorCell>().Mailbox.IsSuspended());

            var values = new int[10];
            var increment = (int)(UInt32.MaxValue / values.Length);

            for (var i = 0; i < values.Length; i++)
                values[i] = Int32.MinValue + increment * i;

            // tell the actor in order
            foreach (var value in values)
            {
                actor.Tell(value);
                actor.Tell(value);
                actor.Tell(value);
            }

            actor.Tell(new StashingActor.Start());

            //resume mailbox, this prevents the mailbox from running to early
            actor.SendSystemMessage(new Resume(null));

            this.Within(5.Seconds(), () =>
            {
                // expect the messages in the original order
                foreach (var value in values)
                {
                    ExpectMsg(value);
                    ExpectMsg(value);
                    ExpectMsg(value);
                }
            });

            ExpectNoMsg(TimeSpan.FromSeconds(0.3));
        }
    }
}

