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
        public class M
        {
            public int Value { get; set; }
        }


        [TestMethod]
        public void ManageSubscriptions()
        {
            var bus = new EventStream(true);
            bus.Subscribe(testActor, typeof(M));
            bus.Publish(new M { Value = 42 });
        }
    }
}
