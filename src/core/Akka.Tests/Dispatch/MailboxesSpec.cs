using System;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Dispatch.SysMsg;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Xunit;

namespace Akka.Tests.Dispatch
{
    public class StringPriorityMailbox : UnboundedPriorityMailbox
    {
        protected override int PriorityGenerator(object message)
        {
            if (message is string)
                return 1;

            return 2;
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
string-prio-mailbox {
    mailbox-type : """ + typeof(StringPriorityMailbox).AssemblyQualifiedName  + @"""
}
";
        }

        [Fact]
        public void CanUseUnboundedPriorityMailbox()
        {
            var actor = Sys.ActorOf(EchoActor.Props(this).WithMailbox("string-prio-mailbox"), "echo");

            //pause mailbox until all messages have been told
            actor.Tell(Suspend.Instance);
            actor.Tell(1);
            actor.Tell(1);
            actor.Tell(1);
            actor.Tell("a");
            actor.Tell(1);
            actor.Tell(new Resume(null));

            //resume mailbox, this prevents the mailbox from running to early
            //priority mailbox is best effort only
            
            ExpectMsg("a");
            ExpectMsg(1);
            ExpectMsg(1);
            ExpectMsg(1);
            ExpectMsg(1);
            ExpectNoMsg(TimeSpan.FromSeconds(1));
        }       
    }
}
