using Microsoft.VisualStudio.TestTools.UnitTesting;
using Pigeon.Configuration;
using Pigeon.Configuration.Hocon;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Tests
{
    [TestClass]
    public class HoconTests
    {
        [TestMethod]
        public void CanTrimValue()
        {
            var hocon = "a= \t \t 1 \t \t,";
            Assert.AreEqual("1", ConfigurationFactory.ParseString(hocon).GetString("a"));
        }

        [TestMethod]
        public void CanConsumeCommaAfterValue()
        {
            var hocon = "a=1,";
            Assert.AreEqual("1", ConfigurationFactory.ParseString(hocon).GetString("a"));
        }

        [TestMethod]
        public void CanAssignIpAddressToField()
        {
            var hocon = @"a=127.0.0.1";
            Assert.AreEqual("127.0.0.1", ConfigurationFactory.ParseString(hocon).GetString("a"));
        }

        [TestMethod]
        public void CanAssignConcatenatedValueToField()
        {
            var hocon = @"a=1 2 3";
            Assert.AreEqual("1 2 3", ConfigurationFactory.ParseString(hocon).GetString("a"));
        }

        [TestMethod]
        public void CanAssignValueToQuotedField()
        {
            var hocon = @"""a""=1";
            Assert.AreEqual(1L, ConfigurationFactory.ParseString(hocon).GetLong("a"));
        }

        [TestMethod]
        public void CanAssignValueToPathExpression()
        {
            var hocon = @"a.b.c=1";
            Assert.AreEqual(1L, ConfigurationFactory.ParseString(hocon).GetLong("a.b.c"));
        }

        [TestMethod]
        public void CanAssignValuesToPathExpressions()
        {
            var hocon = @"
a.b.c=1
a.b.d=2
a.b.e.f=3
";
            Assert.AreEqual(1L, ConfigurationFactory.ParseString(hocon).GetLong("a.b.c"));
            Assert.AreEqual(2L, ConfigurationFactory.ParseString(hocon).GetLong("a.b.d"));
            Assert.AreEqual(3L, ConfigurationFactory.ParseString(hocon).GetLong("a.b.e.f"));
        }

        [TestMethod]
        public void CanAssignLongToField()
        {
            var hocon = @"a=1";
            Assert.AreEqual(1L, ConfigurationFactory.ParseString(hocon).GetLong("a"));
        }

        [TestMethod]
        public void CanAssignArrayToField()
        {
            var hocon = @"a=
[
    1
    2
    3
]";
        //TODO: add array api
        //    Assert.AreEqual(1L, ConfigurationFactory.ParseString(hocon).GetLong("a"));
        }

        [TestMethod]
        public void CanAssignDoubleToField()
        {
            var hocon = @"a=1.1";
            Assert.AreEqual(1.1, ConfigurationFactory.ParseString(hocon).GetDouble("a"));
        }

        [TestMethod]
        public void CanAssignBooleanToField()
        {
            var hocon = @"a=true";
            Assert.AreEqual(true, ConfigurationFactory.ParseString(hocon).GetBoolean("a"));
            hocon = @"a=false";
            Assert.AreEqual(false, ConfigurationFactory.ParseString(hocon).GetBoolean("a"));
        }

        [TestMethod]
        public void CanAssignQuotedStringToField()
        {
            var hocon = @"a=""hello""";
            Assert.AreEqual("hello", ConfigurationFactory.ParseString(hocon).GetString("a"));           
        }

        [TestMethod]
        public void CanAssignUnQuotedStringToField()
        {
            var hocon = @"a=hello";
            Assert.AreEqual("hello", ConfigurationFactory.ParseString(hocon).GetString("a"));
        }

        [TestMethod]
        public void CanAssignTrippleQuotedStringToField()
        {
            var hocon = @"a=""""""hello""""""";
            Assert.AreEqual("hello", ConfigurationFactory.ParseString(hocon).GetString("a"));
        }
    }
}

