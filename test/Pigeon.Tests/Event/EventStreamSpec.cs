using Microsoft.VisualStudio.TestTools.UnitTesting;
using Pigeon.Event;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Tests.Event
{
    [TestClass]
    public class EventStreamSpec : AkkaSpec
    {
        
        public class M : Comparable
        {
            public int Value { get; set; }           
        }

        public class A : Comparable
        {

        }

        public class B1 : A
        {
            
        }

        public class B2 : A
        {
            
        }

        public class C : B1
        {
            //oh dear.. we should go for F# for this...
        }


        [TestMethod]
        public void ManageSubscriptions()
        {
            var bus = new EventStream(true);
            bus.Subscribe(testActor, typeof(M));

            bus.Publish(new M { Value = 42 });
            expectMsg(new M { Value = 42 });
            bus.Unsubscribe(testActor);
            bus.Publish(new M { Value = 43 });
            expectNoMsg(TimeSpan.FromSeconds(1));
        }

        [TestMethod]
        public void NotAllowNullAsSubscriber()
        {
            var bus = new EventStream(true);
            intercept<ArgumentNullException>(() =>
            {
                bus.Subscribe(null, typeof(M));
            });            
        }

        [TestMethod]
        public void NotAllowNullAsUnsubscriber()
        {
            var bus = new EventStream(true);
            intercept<ArgumentNullException>(() =>
            {
                bus.Unsubscribe(null, typeof(M));
            });
            intercept<ArgumentNullException>(() =>
            {
                bus.Unsubscribe(null);
            });
        }

        [TestMethod]
        public void ManageSubChannelsUsingClasses()
        {
            var a = new A();
            var b1 = new B1();
            var b2 = new B2();
            var c = new C();
            var bus = new EventStream(false);
            bus.Subscribe(testActor,typeof(B2));
            bus.Publish(c);
            bus.Publish(b2);
            expectMsg(b2);
            bus.Subscribe(testActor, typeof(A));
            bus.Publish(c);
            expectMsg(c);
            bus.Publish(b1);
            expectMsg(b1);

            //TODO: we can't unsubscribe from types we havent subscribed to.
            //we have subscribed to base class A, but this implies that the type B1 and subclasses should no longer match on the A entry

            bus.Unsubscribe(testActor, typeof(B1));
            bus.Publish(c); //should not publish
            bus.Publish(b2); //should publish
            bus.Publish(a); //should publish
            expectMsg(b2);
            //expectMsg(a);
            //expectNoMsg(TimeSpan.FromSeconds(1));
        }
    }
}
