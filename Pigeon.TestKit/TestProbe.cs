using Microsoft.VisualStudio.TestTools.UnitTesting;
using Pigeon.Actor;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Pigeon.Tests
{
    public class TestProbeActorRef : ActorRef
    {
        private TestProbe owner;
        public TestProbeActorRef(TestProbe owner)
        {
            this.owner = owner;
        }

        protected override void TellInternal(object message, ActorRef sender)
        {
            owner.Tell(message, sender);
        }        
    }
    public class TestProbe
    {
        private BlockingCollection<object> queue = new BlockingCollection<object>();
        public TestProbe()
        {
            this.Ref = new TestProbeActorRef(this);
        }

        public ActorRef Ref { get;private set; }

        public void expectMsg(object expected)
        {
            var res = queue.Take();
            Assert.AreEqual(expected, res);
        }

        public void Tell(object message, ActorRef sender)
        {
            queue.Add(message);
        }

        public void expectNoMsg(TimeSpan duration)
        {
            object res;
            if (queue.TryTake(out res,duration))
            {
                Assert.Fail("Did not expect a message during the duration " + duration.ToString());
            }
        }
    }
}
