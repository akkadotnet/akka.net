using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
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

            system.DeadLetters.Tell("test");
            
        }
    }
}
