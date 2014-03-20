﻿using Microsoft.VisualStudio.TestTools.UnitTesting;
using Akka.Actor;
using Akka.Configuration;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Tests
{
    [TestClass]
    public class ActorCellSpec : AkkaSpec
    {
        public class SomeUserMessage : Comparable
        {
            public string A { get; set; }
            public int B { get; set; }
            public Guid C { get; set; }
        }
       [TestMethod]
       public void DoesNotSerializesUserMessagesWhenSerializeAllMessagesIsOff()
       {
            var message = new SomeUserMessage
            {
                A = "abc",
                B = 123,
                C = Guid.Empty
            };
            testActor.Tell(message);

            var result = queue.Take();

            Assert.IsFalse(sys.Settings.SerializeAllMessages);
            Assert.AreEqual(message, result);
            Assert.AreSame(message, result);
        }

       [TestMethod]
       public void SerializesUserMessagesWhenSerializeAllMessagesIsOn()
       {
           var config = ConfigurationFactory.ParseString(@"akka.actor.serialize-messages = on");
           var sys = ActorSystem.Create("test",config);
           testActor = sys.ActorOf(Props.Create(() => new TestActor(queue,messages)));      

           var message = new SomeUserMessage
           {
               A = "abc",
               B = 123,
               C = Guid.Empty
           };
           testActor.Tell(message);

           var result = queue.Take();

           Assert.IsTrue(sys.Settings.SerializeAllMessages);
           Assert.AreEqual(message, result);
           Assert.AreNotSame(message, result);
       }
    }
}
