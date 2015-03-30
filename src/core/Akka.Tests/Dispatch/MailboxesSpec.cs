using System;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Dispatch.SysMsg;
using Akka.TestKit;
using Akka.TestKit.TestActors;
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
";
        }

        [Fact]
        public void CanUseUnboundedPriorityMailbox()
        {
            var actor = Sys.ActorOf(EchoActor.Props(this).WithMailbox("string-prio-mailbox"), "echo");

            //pause mailbox until all messages have been told
            actor.Tell(Suspend.Instance);

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
            actor.Tell(new Resume(null));

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
    }
}
