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
        public void CanParseHocon()
        {
            var hocon = @"
root {
  int = 1
  quoted-string = ""foo""
  unquoted-string = bar
  concat-string = foo bar
  object {
    hasContent = true
  }
  array = [1,2,3,4]
  array-concat = [[1,2] [2,3]]
  array-single-element = [1 2 3 4]
  array-newline-element = [
    1
    2
    3
    4
  ]
  null = null
  double = 1.23
  bool = true
}
";
            var config = ConfigurationFactory.ParseString(hocon);
            Assert.AreEqual("1", config.GetString("root.int"));
            Assert.AreEqual("1.23", config.GetString("root.double"));
            Assert.AreEqual(true, config.GetBoolean("root.bool"));
            Assert.AreEqual(true, config.GetBoolean("root.object.hasContent"));
            Assert.AreEqual(null, config.GetString("root.null"));
            Assert.AreEqual("foo", config.GetString("root.quoted-string"));
            Assert.AreEqual("bar", config.GetString("root.unquoted-string"));
            Assert.AreEqual("foo bar", config.GetString("root.concat-string"));
        }

        [TestMethod]
        public void CanParseJson()
        {
            var hocon = @"
""root"" : {
  ""int"" : 1,
  ""string"" : ""foo"",
  ""object"" : {
        ""hasContent"" : true
    },
  ""array"" : [1,2,3,4],
  ""null"" : null,
  ""double"" : 1.23,
  ""bool"" : true
}
";
            var config = ConfigurationFactory.ParseString(hocon);
            Assert.AreEqual("1", config.GetString("root.int"));
            Assert.AreEqual("1.23", config.GetString("root.double"));
            Assert.AreEqual(true, config.GetBoolean("root.bool"));
            Assert.AreEqual(true, config.GetBoolean("root.object.hasContent"));
            Assert.AreEqual(null, config.GetString("root.null"));
            Assert.AreEqual("foo", config.GetString("root.string"));
        }

        [TestMethod]
        public void CanMergeObject()
        {
            var hocon = @"
a.b.c = {
        x = 1
        y = 2
    }
a.b.c = {
        z = 3
    }
";
            var config = ConfigurationFactory.ParseString(hocon);
            Assert.AreEqual("1", config.GetString("a.b.c.x"));
            Assert.AreEqual("2", config.GetString("a.b.c.y"));
            Assert.AreEqual("3", config.GetString("a.b.c.z"));
        }

        [TestMethod]
        public void CanOverrideObject()
        {
            var hocon = @"
a.b.c = {
        x = 1
        y = 2
    }
a.b.c = null
a.b.c = {
        z = 3
    }
";
            var config = ConfigurationFactory.ParseString(hocon);
            Assert.AreEqual(null, config.GetString("a.b.c.x"));
            Assert.AreEqual(null, config.GetString("a.b.c.y"));
            Assert.AreEqual("3", config.GetString("a.b.c.z"));
        }

        [TestMethod]
        public void CanParseObject()
        {
            var hocon = @"
a {
  b = 1
}
";
            Assert.AreEqual("1", ConfigurationFactory.ParseString(hocon).GetString("a.b"));
        }

        [TestMethod]
        public void CanTrimValue()
        {
            var hocon = "a= \t \t 1 \t \t,";
            Assert.AreEqual("1", ConfigurationFactory.ParseString(hocon).GetString("a"));
        }

        [TestMethod]
        public void CanTrimConcatenatedValue()
        {
            var hocon = "a= \t \t 1 2 3 \t \t,";
            Assert.AreEqual("1 2 3", ConfigurationFactory.ParseString(hocon).GetString("a"));
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
            var config = ConfigurationFactory.ParseString(hocon);
            Assert.AreEqual(1L, config.GetLong("a.b.c"));
            Assert.AreEqual(2L, config.GetLong("a.b.d"));
            Assert.AreEqual(3L, config.GetLong("a.b.e.f"));
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
        public void CanAssignNullToField()
        {
            var hocon = @"a=null";
            Assert.IsNull(ConfigurationFactory.ParseString(hocon).GetString("a"));
        }

        [TestMethod]
        public void CanAssignBooleanToField()
        {
            var hocon = @"a=true";
            Assert.AreEqual(true, ConfigurationFactory.ParseString(hocon).GetBoolean("a"));
            hocon = @"a=false";
            Assert.AreEqual(false, ConfigurationFactory.ParseString(hocon).GetBoolean("a"));

            hocon = @"a=on";
            Assert.AreEqual(true, ConfigurationFactory.ParseString(hocon).GetBoolean("a"));
            hocon = @"a=off";
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

