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
        public void CanAssignLongToField()
        {
            var hocon = @"a=1";
            Assert.AreEqual(1L, ConfigurationFactory.ParseString(hocon).GetLong("a"));
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

