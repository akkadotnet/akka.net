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

        public override void Resume(Exception causedByFailure = null)
        {
            throw new NotImplementedException();
        }

        public override void Stop()
        {
            throw new NotImplementedException();
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

        internal void expectMsg(object expected)
        {
            var res = queue.Take();
            Assert.AreEqual(expected, res);
        }

        internal void Tell(object message, ActorRef sender)
        {
            queue.Add(message);
        }
    }
}
