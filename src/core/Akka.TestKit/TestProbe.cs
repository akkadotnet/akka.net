﻿using Xunit;
using Akka.Actor;
using System;
using System.Collections.Concurrent;

namespace Akka.Tests
{
    public class TestProbeActorRef : ActorRef
    {
        public static AtomicInteger TestActorId =  new AtomicInteger(0);

        private readonly TestProbe _owner;
        private readonly ActorPath _path=new RootActorPath(Address.AllSystems,"/TestProbe" + TestActorId.GetAndIncrement());

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

        public void expectMsgType<T>()
        {
            var res = queue.Take();
            Assert.True(res is T);
        }

        public List<object> ReceiveN(int n)
        {
            var res = new List<object>();
            for (var i = 0; i < n; i++)
            {
                res.Add(queue.Take());
            }
            return res;
        }
    }
}
