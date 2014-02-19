using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Tests.Actor
{
    [TestClass]
    public class ActorSelectionSpec : AkkaSpec
    {
        [TestMethod()]
        public void CanResolveChildPath()
        {
            var selection = sys.ActorSelection("user/test");
            selection.Tell("hello");
            expectMsg("hello");
        }

        [TestMethod()]
        public void CanResolveUpAndDownPath()
        {
            var selection = sys.ActorSelection("user/test/../../user/test");
            selection.Tell("hello");
            expectMsg("hello");
        }
    }
}
