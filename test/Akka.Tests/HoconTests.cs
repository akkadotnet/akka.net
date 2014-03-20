using Microsoft.VisualStudio.TestTools.UnitTesting;
using Akka.Configuration;
using Akka.Configuration.Hocon;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Tests
{
    [TestClass]
    public class HoconTests
    {
        //Added tests to conform to the HOCON spec https://github.com/typesafehub/config/blob/master/HOCON.md
        [TestMethod]
        public void CanUsePathsAsKeys_3_14()
        {
            var hocon1 = @"3.14 : 42";
            var hocon2 = @"3 { 14 : 42}";
            Assert.AreEqual(ConfigurationFactory.ParseString(hocon1).GetString("3.14"), ConfigurationFactory.ParseString(hocon2).GetString("3.14"));
        }

        [TestMethod]
        public void CanUsePathsAsKeys_3()
        {
            var hocon1 = @"3 : 42";
            var hocon2 = @"""3"" : 42";
            Assert.AreEqual(ConfigurationFactory.ParseString(hocon1).GetString("3"), ConfigurationFactory.ParseString(hocon2).GetString("3"));
        }

        [TestMethod]
        public void CanUsePathsAsKeys_true()
        {
            var hocon1 = @"true : 42";
            var hocon2 = @"""true"" : 42";
            Assert.AreEqual(ConfigurationFactory.ParseString(hocon1).GetString("true"), ConfigurationFactory.ParseString(hocon2).GetString("true"));
        }

        [TestMethod]
        public void CanUsePathsAsKeys_FooBar()
        {
            var hocon1 = @"foo.bar : 42";
            var hocon2 = @"foo { bar : 42 }";
            Assert.AreEqual(ConfigurationFactory.ParseString(hocon1).GetString("foo.bar"), ConfigurationFactory.ParseString(hocon2).GetString("foo.bar"));
        }

        [TestMethod]
        public void CanUsePathsAsKeys_FooBarBaz()
        {
            var hocon1 = @"foo.bar.baz : 42";
            var hocon2 = @"foo { bar { baz : 42 } }";
            Assert.AreEqual(ConfigurationFactory.ParseString(hocon1).GetString("foo.bar.baz"), ConfigurationFactory.ParseString(hocon2).GetString("foo.bar.baz"));
        }

        [TestMethod]
        public void CanUsePathsAsKeys_AX_AY()
        {
            var hocon1 = @"a.x : 42, a.y : 43";
            var hocon2 = @"a { x : 42, y : 43 }";
            Assert.AreEqual(ConfigurationFactory.ParseString(hocon1).GetString("a.x"), ConfigurationFactory.ParseString(hocon2).GetString("a.x"));
            Assert.AreEqual(ConfigurationFactory.ParseString(hocon1).GetString("a.y"), ConfigurationFactory.ParseString(hocon2).GetString("a.y"));
        }

        [TestMethod]
        public void CanUsePathsAsKeys_A_B_C()
        {
            var hocon1 = @"a b c : 42";
            var hocon2 = @"""a b c"" : 42";
            Assert.AreEqual(ConfigurationFactory.ParseString(hocon1).GetString("a b c"), ConfigurationFactory.ParseString(hocon2).GetString("a b c"));
        }


        [TestMethod]
        public void CanConcatinateSubstitutedUnquotedString()
        {
            var hocon = @"a {
  name = Roger
  c = Hello my name is ${a.name}
}";
            Assert.AreEqual("Hello my name is Roger",ConfigurationFactory.ParseString(hocon).GetString("a.c"));
        }

        [TestMethod]
        public void CanConcatinateSubstitutedArray()
        {
            var hocon = @"a {
  b = [1,2,3]
  c = ${a.b} [4,5,6]
}";
            Assert.IsTrue(new[] { 1, 2, 3, 4, 5, 6 }.SequenceEqual(ConfigurationFactory.ParseString(hocon).GetIntList("a.c")));
        }

        [TestMethod]
        public void CanParseSubConfig()
        {
            var hocon = @"
a {
   b {
     c = 1
     d = true
   }
}";
            var config = ConfigurationFactory.ParseString(hocon);
            var subConfig = config.GetConfig("a");
            Assert.AreEqual(1, subConfig.GetInt("b.c"));
            Assert.AreEqual(true, subConfig.GetBoolean("b.d"));
        }


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
  array-concat = [[1,2] [3,4]]
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
            Assert.IsTrue(new[] { 1, 2, 3,4 }.SequenceEqual(ConfigurationFactory.ParseString(hocon).GetIntList("root.array")));
            Assert.IsTrue(new[] { 1, 2, 3, 4 }.SequenceEqual(ConfigurationFactory.ParseString(hocon).GetIntList("root.array-newline-element")));
            Assert.IsTrue(new[] { "1 2 3 4" }.SequenceEqual(ConfigurationFactory.ParseString(hocon).GetStringList("root.array-single-element")));
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
  ""array"" : [1,2,3],
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
            Assert.IsTrue(new[] { 1, 2, 3 }.SequenceEqual(ConfigurationFactory.ParseString(hocon).GetIntList("root.array")));

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
a.b = 1
a = null
a.c = 3
";
            var config = ConfigurationFactory.ParseString(hocon);
            Assert.AreEqual(null, config.GetString("a.b"));
            Assert.AreEqual("3", config.GetString("a.c"));
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
            Assert.IsTrue(new[] { 1, 2, 3 }.SequenceEqual(ConfigurationFactory.ParseString(hocon).GetIntList("a")));

            //hocon = @"a= [ 1, 2, 3 ]";
            //Assert.IsTrue(new[] { 1, 2, 3 }.SequenceEqual(ConfigurationFactory.ParseString(hocon).GetIntList("a")));
        }

        [TestMethod]
        public void CanConcatenateArray()
        {
            var hocon = @"a=[1,2] [3,4]";
            Assert.IsTrue(new[] { 1, 2, 3, 4 }.SequenceEqual(ConfigurationFactory.ParseString(hocon).GetIntList("a")));
        }

        [TestMethod]
        public void CanAssignSubstitutionToField()
        {
            var hocon = @"a{
    b = 1
    c = ${a.b}
    d = ${a.c}23
}";
            Assert.AreEqual(1, ConfigurationFactory.ParseString(hocon).GetInt("a.c"));
            Assert.AreEqual(123, ConfigurationFactory.ParseString(hocon).GetInt("a.d"));
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

