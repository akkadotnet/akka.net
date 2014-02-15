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


        interface T {}
        interface AT : T{}
        interface  ATT : AT{}
        interface  BT : T{}
        interface  BTT : BT{}
        class CC {}
        class CCATBT : CC, ATT, BTT { }

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

            bus.Unsubscribe(testActor, typeof(B1));
            bus.Publish(c); //should not publish
            bus.Publish(b2); //should publish
            bus.Publish(a); //should publish
            expectMsg(b2);
            expectMsg(a);
            expectNoMsg(TimeSpan.FromSeconds(1));
        }


        [Description("manage sub-channels using classes and traits (update on subscribe)" )]
        [TestMethod]
        public void ManageSubChannelsUsingClassesAndInterfacesUpdateOnSubscribe()
        {
            var es = new EventStream(false);
            var tm1 = new CC();
            var tm2 = new CCATBT();
            var a1= TestProbe();
            var a2= TestProbe();
            var a3 = TestProbe();
            var a4 = TestProbe();

            es.Subscribe(a1.Ref, typeof(AT));
            es.Subscribe(a2.Ref, typeof(BT)) ;
            es.Subscribe(a3.Ref, typeof(CC));
            es.Subscribe(a4.Ref, typeof(CCATBT)) ;
            es.Publish(tm1);
            es.Publish(tm2);
            a1.expectMsg(tm2);
            a2.expectMsg(tm2);
            a3.expectMsg(tm1);
            a3.expectMsg(tm2); 
            a4.expectMsg(tm2);
            es.Unsubscribe(a1.Ref, typeof(AT)).Then(Assert.IsTrue);
            es.Unsubscribe(a2.Ref, typeof(BT)).Then(Assert.IsTrue);
            es.Unsubscribe(a3.Ref, typeof(CC)).Then(Assert.IsTrue);
            es.Unsubscribe(a4.Ref, typeof(CCATBT)).Then(Assert.IsTrue);
        }

        //"manage sub-channels using classes and traits (update on unsubscribe)"
        [TestMethod]
        public void ManageSubChannelsUsingClassesAndInterfacesUpdateOnUnsubscribe()
        {
            var es = new EventStream(false);
            var tm1 = new CC();
            var tm2 = new CCATBT();
            var a1 = TestProbe();
            var a2 = TestProbe();
            var a3 = TestProbe();
            var a4 = TestProbe();

            es.Subscribe(a1.Ref, typeof(AT));
            es.Subscribe(a2.Ref, typeof(BT));
            es.Subscribe(a3.Ref, typeof(CC));
            es.Subscribe(a4.Ref, typeof(CCATBT));
            es.Unsubscribe(a3.Ref, typeof(CC));
            es.Publish(tm1);
            es.Publish(tm2);
            a1.expectMsg(tm2);
            a2.expectMsg(tm2);
            a3.expectNoMsg(TimeSpan.FromSeconds(1));
            a4.expectMsg(tm2);
            es.Unsubscribe(a1.Ref, typeof(AT)).Then(Assert.IsTrue);
            es.Unsubscribe(a2.Ref, typeof(BT)).Then(Assert.IsTrue);
            es.Unsubscribe(a3.Ref, typeof(CC)).Then(Assert.IsFalse);
            es.Unsubscribe(a4.Ref, typeof(CCATBT)).Then(Assert.IsTrue);
        }

        [TestMethod]
        public void ManageSubChannelsUsingClassesAndInterfacesUpdateOnUnsubscribeAll()
        {
            var es = new EventStream(false);
            var tm1 = new CC();
            var tm2 = new CCATBT();
            var a1 = TestProbe();
            var a2 = TestProbe();
            var a3 = TestProbe();
            var a4 = TestProbe();

            es.Subscribe(a1.Ref, typeof(AT)).Then(Assert.IsTrue);
            es.Subscribe(a2.Ref, typeof(BT)).Then(Assert.IsTrue);
            es.Subscribe(a3.Ref, typeof(CC)).Then(Assert.IsTrue);
            es.Subscribe(a4.Ref, typeof(CCATBT)).Then(Assert.IsTrue);
            es.Unsubscribe(a3.Ref).Then(Assert.IsTrue);
            es.Publish(tm1);
            es.Publish(tm2);
            a1.expectMsg(tm2);
            a2.expectMsg(tm2);
            a3.expectNoMsg(TimeSpan.FromSeconds(1));
            a4.expectMsg(tm2);
            es.Unsubscribe(a1.Ref, typeof(AT)).Then(Assert.IsTrue);
            es.Unsubscribe(a2.Ref, typeof(BT)).Then(Assert.IsTrue);
            es.Unsubscribe(a3.Ref, typeof(CC)).Then(Assert.IsFalse);
            es.Unsubscribe(a4.Ref, typeof(CCATBT)).Then(Assert.IsTrue);
        }
    }
}
