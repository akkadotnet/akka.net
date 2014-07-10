using Xunit;
using Akka.Actor;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Akka.Tests
{
    public class TestProbeActorRef : ActorRef
    {
        private readonly TestProbe _owner;
        private readonly ActorPath _path=new RootActorPath(Address.AllSystems,"/TestProbe");

        public TestProbeActorRef(TestProbe owner)
        {
            _owner = owner;
        }

        public override ActorPath Path
        {
            get { return _path; }
        }

        protected override void TellInternal(object message, ActorRef sender)
        {
            _owner.Tell(message, sender);
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
            Assert.Equal(expected, res);
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
                Assert.True(false, "Did not expect a message during the duration " + duration.ToString());
            }
        }
    }
}
