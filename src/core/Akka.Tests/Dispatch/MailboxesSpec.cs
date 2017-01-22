//-----------------------------------------------------------------------
// <copyright file="MailboxesSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Dispatch.SysMsg;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Akka.Util.Internal;
using Xunit;

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
";
        }

        [Fact]
        public void Can_use_unbounded_priority_mailbox()
        {
            var actor = (IInternalActorRef)Sys.ActorOf(EchoActor.Props(this).WithMailbox("string-prio-mailbox"), "echo");

            //pause mailbox until all messages have been told
            actor.SendSystemMessage(new Suspend());

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
    }
}

