//-----------------------------------------------------------------------
// <copyright file="MailboxesSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Xunit;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Dispatch.MessageQueues;
using Akka.Util.Internal;

namespace Akka.Tests.Dispatch
{
    public class ActorMailboxSpec : AkkaSpec
    {
        #region Test configuration

        public ActorMailboxSpec() : base(GetConfig())
        {

        }

        private static string GetConfig()
        {
            return @"

unbounded-dispatcher {
    mailbox-type = """ + typeof(UnboundedDequeBasedMailbox).AssemblyQualifiedName + @"""
}

task-dispatcher {
    type = """ + typeof(TaskDispatcherConfigurator).AssemblyQualifiedName + @"""
}

unbounded-mailbox {
    mailbox-type = """ + typeof(UnboundedDequeBasedMailbox).AssemblyQualifiedName + @"""
}

unbounded-deque-mailbox {
    mailbox-type = """ + typeof(UnboundedDequeBasedMailbox).AssemblyQualifiedName + @"""
}

bounded-mailbox {
    mailbox-capacity = 1000
    mailbox-push-timeout-time = 10s
    mailbox-type = """ + typeof(BoundedDequeBasedMailbox).AssemblyQualifiedName + @"""
}

akka.actor.deployment {
    /default-bounded {
        mailbox = bounded-mailbox
    }

    /default-unboundeded-deque {
        mailbox = unbounded-deque-mailbox
    }

    /unbounded-default { 
        dispatcher = unbounded-dispatcher 
    } 
}";
        }

        #endregion

        #region Test helpers methods and classes

        public class MailboxReportingActor : ReceiveActor
        {
            public MailboxReportingActor()
            {
                ReceiveAny(x => Sender.Tell(((ActorCell)Context).Mailbox));
            }
        }

        public class StashMailboxReportingActor : MailboxReportingActor, IWithUnboundedStash
        {
            public IStash Stash { get; set; }
        }

        public class StashMailboxWithParamsReportingActor : MailboxReportingActor, IWithUnboundedStash
        {
            public StashMailboxWithParamsReportingActor(int param1, string param2)
            {

            }

            public IStash Stash { get; set; }
        }

        public class BoundedQueueReportingActor : MailboxReportingActor, IRequiresMessageQueue<IBoundedMessageQueueSemantics>
        {

        }

        private Mailbox CheckMailBoxType(Props props, string name, IEnumerable<Type> expectedMailboxTypes)
        {
            var actor = Sys.ActorOf(props, name);
            actor.Tell("ping");

            var mailbox = ExpectMsg<Mailbox>(msg =>
            {
                expectedMailboxTypes.ForEach(type => Assert.True(type.IsAssignableFrom(msg.GetType()),
                    String.Format(CultureInfo.InvariantCulture, "Type [{0}] is not assignable to [{1}]", msg.GetType(), type)));
            });

            return mailbox;
        }

        #endregion

        #region Test cases

        [Fact(DisplayName = @"Actors get an unbounded mailbox by default")]
        public void Actors_get_unbounded_mailbox_by_default()
        {
            CheckMailBoxType(Props.Create<MailboxReportingActor>(), "default-default", new[] { typeof(UnboundedMailbox) });
        }

        [Fact(DisplayName = @"Actors get an unbounded deque message queue when it is only configured on the props")]
        public void Actors_get_unbounded_dequeue_mailbox_when_configured_in_properties()
        {
            CheckMailBoxType(Props.Create<MailboxReportingActor>().WithMailbox("unbounded-mailbox"),
                "default-override-from-props", new[] { typeof(UnboundedDequeBasedMailbox) });
        }

        [Fact(DisplayName = @"Actors get an unbounded deque message queue when it's only mixed with Stash")]
        public void Actors_get_unbounded_mailbox_when_configured_with_stash_only()
        {
            CheckMailBoxType(Props.Create<StashMailboxReportingActor>(),
                "default-override-from-stash", new[] { typeof(UnboundedDequeBasedMailbox) });

            CheckMailBoxType(Props.Create(() => new StashMailboxReportingActor()),
                "default-override-from-stash2", new[] { typeof(UnboundedDequeBasedMailbox) });

            CheckMailBoxType(Props.Create<StashMailboxWithParamsReportingActor>(10, "foo"),
                "default-override-from-stash3", new[] { typeof(UnboundedDequeBasedMailbox) });

            CheckMailBoxType(Props.Create(() => new StashMailboxWithParamsReportingActor(10, "foo")),
                "default-override-from-stash4", new[] { typeof(UnboundedDequeBasedMailbox) });
        }

        [Fact(DisplayName = "Actors get an unbounded deque message queue when it's configured as mailbox")]
        public void Actors_get_unbounded_dequeue_message_queue_when_configured_as_mailbox()
        {
            CheckMailBoxType(Props.Create<MailboxReportingActor>(), "default-unboundeded-deque",
                new[] { typeof(UnboundedDequeBasedMailbox) });
        }

        [Fact(DisplayName = "Actor get an unbounded message queue when defined in dispatcher")]
        public void Actor_gets_configured_mailbox_from_dispatcher()
        {
            CheckMailBoxType(Props.Create<MailboxReportingActor>(), "unbounded-default",
                new[] { typeof(UnboundedDequeBasedMailbox) });
        }

        [Fact(DisplayName = "get an unbounded message queue with a balancing dispatcher")]
        public void Actors_gets_unbounded_mailbox_with_task_dispatcher()
        {
            CheckMailBoxType(Props.Create<MailboxReportingActor>().WithDispatcher("task-dispatcher"),
                "unbounded-tasks", new[] { typeof(UnboundedMailbox) });
        }
        
        #endregion
    }
}
