using System;
using Akka.Actor;
using Akka.Dispatch;
using Akka.TestKit;
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
            var actor = Sys.ActorOf(Props.Create(() => new SomeActor(TestActor)).WithMailbox("string-prio-mailbox"));
            actor.Tell(1);
            actor.Tell(1);
            actor.Tell(1);
            actor.Tell("a");
            actor.Tell(1);
            
            //this is racy, mailbox could get to run too early and pass one of the integers before the string
            //TODO: how do I test this better?

            ExpectMsg("a");
            ExpectMsg(1);
            ExpectMsg(1);
            ExpectMsg(1);
            ExpectMsg(1);
            ExpectNoMsg(TimeSpan.FromSeconds(1));
        }

        public class SomeActor : UntypedActor
        {
            private readonly ActorRef _callback;
            public SomeActor(ActorRef callback)
            {
                _callback = callback;
            }

            protected override void OnReceive(object message)
            {
                _callback.Tell(message);
            }
        }
    }
}
