using Microsoft.VisualStudio.TestTools.UnitTesting;
using Pigeon.Event;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Tests
{
    [TestClass]
    public class DeadLettersSpec : AkkaSpec
    {
        [TestMethod]
        public void CanSendMessagesToDeadLetters()
        {
            var stream = new BlockingCollection<EventMessage>();
            system.EventStream.Subscribe(new BlockingCollectionSubscriber(stream));
            system.DeadLetters.Tell("foobar");
            var message = stream.Take();
            Assert.IsInstanceOfType(message, typeof(DeadLetter));
            var deadLetter = (DeadLetter)message;
            var payload = (string)deadLetter.Message;
            Assert.AreEqual("foobar", payload);
        }
    }
}
