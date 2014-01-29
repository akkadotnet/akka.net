using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Pigeon.Hocon.Tests
{
    [TestClass]
    public class UnitTest1
    {
        [TestMethod]
        public void AssignNumber()
        {
            var hocon = "a = 1";

            var res = Parser.Parse(hocon).ToFlatString();

            Assert.AreEqual(" {a = 1}",res);
        }

        [TestMethod]
        public void AssignString()
        {
            var hocon = "a = \"hello\" ";

            var res = Parser.Parse(hocon).ToFlatString();

            Assert.AreEqual(" {a = \"hello\"}", res);
        }

        [TestMethod]
        public void AssignPropertyPath()
        {
            var hocon = "a.b = 1";

            var res = Parser.Parse(hocon).ToFlatString();

            Assert.AreEqual(" {a {b = 1}}", res);
        }

        
    }
}
