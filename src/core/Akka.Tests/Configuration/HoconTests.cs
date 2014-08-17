using System.Linq;
using Akka.Configuration;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Configuration
{
    public class HoconTests
    {
        //Added tests to conform to the HOCON spec https://github.com/typesafehub/config/blob/master/HOCON.md
        [Fact]
        public void CanUsePathsAsKeys_3_14()
        {
            string hocon1 = @"3.14 : 42";
            string hocon2 = @"3 { 14 : 42}";
            Assert.Equal(ConfigurationFactory.ParseString(hocon1).GetString("3.14"),
                ConfigurationFactory.ParseString(hocon2).GetString("3.14"));
        }

        [Fact]
        public void CanUsePathsAsKeys_3()
        {
            string hocon1 = @"3 : 42";
            string hocon2 = @"""3"" : 42";
            Assert.Equal(ConfigurationFactory.ParseString(hocon1).GetString("3"),
                ConfigurationFactory.ParseString(hocon2).GetString("3"));
        }

        [Fact]
        public void CanUsePathsAsKeys_true()
        {
            string hocon1 = @"true : 42";
            string hocon2 = @"""true"" : 42";
            Assert.Equal(ConfigurationFactory.ParseString(hocon1).GetString("true"),
                ConfigurationFactory.ParseString(hocon2).GetString("true"));
        }

        [Fact]
        public void CanUsePathsAsKeys_FooBar()
        {
            string hocon1 = @"foo.bar : 42";
            string hocon2 = @"foo { bar : 42 }";
            Assert.Equal(ConfigurationFactory.ParseString(hocon1).GetString("foo.bar"),
                ConfigurationFactory.ParseString(hocon2).GetString("foo.bar"));
        }

        [Fact]
        public void CanUsePathsAsKeys_FooBarBaz()
        {
            string hocon1 = @"foo.bar.baz : 42";
            string hocon2 = @"foo { bar { baz : 42 } }";
            Assert.Equal(ConfigurationFactory.ParseString(hocon1).GetString("foo.bar.baz"),
                ConfigurationFactory.ParseString(hocon2).GetString("foo.bar.baz"));
        }

        [Fact]
        public void CanUsePathsAsKeys_AX_AY()
        {
            string hocon1 = @"a.x : 42, a.y : 43";
            string hocon2 = @"a { x : 42, y : 43 }";
            Assert.Equal(ConfigurationFactory.ParseString(hocon1).GetString("a.x"),
                ConfigurationFactory.ParseString(hocon2).GetString("a.x"));
            Assert.Equal(ConfigurationFactory.ParseString(hocon1).GetString("a.y"),
                ConfigurationFactory.ParseString(hocon2).GetString("a.y"));
        }

        [Fact]
        public void CanUsePathsAsKeys_A_B_C()
        {
            string hocon1 = @"a b c : 42";
            string hocon2 = @"""a b c"" : 42";
            Assert.Equal(ConfigurationFactory.ParseString(hocon1).GetString("a b c"),
                ConfigurationFactory.ParseString(hocon2).GetString("a b c"));
        }


        [Fact]
        public void CanConcatinateSubstitutedUnquotedString()
        {
            string hocon = @"a {
  name = Roger
  c = Hello my name is ${a.name}
}";
            Assert.Equal("Hello my name is Roger", ConfigurationFactory.ParseString(hocon).GetString("a.c"));
        }

        [Fact]
        public void CanConcatinateSubstitutedArray()
        {
            string hocon = @"a {
  b = [1,2,3]
  c = ${a.b} [4,5,6]
}";
            Assert.True(new[] {1, 2, 3, 4, 5, 6}.SequenceEqual(ConfigurationFactory.ParseString(hocon).GetIntList("a.c")));
        }

        [Fact]
        public void CanParseSubConfig()
        {
            string hocon = @"
a {
   b {
     c = 1
     d = true
   }
}";
            Config config = ConfigurationFactory.ParseString(hocon);
            Config subConfig = config.GetConfig("a");
            Assert.Equal(1, subConfig.GetInt("b.c"));
            Assert.Equal(true, subConfig.GetBoolean("b.d"));
        }


        [Fact]
        public void CanParseHocon()
        {
            string hocon = @"
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
            Config config = ConfigurationFactory.ParseString(hocon);
            Assert.Equal("1", config.GetString("root.int"));
            Assert.Equal("1.23", config.GetString("root.double"));
            Assert.Equal(true, config.GetBoolean("root.bool"));
            Assert.Equal(true, config.GetBoolean("root.object.hasContent"));
            Assert.Equal(null, config.GetString("root.null"));
            Assert.Equal("foo", config.GetString("root.quoted-string"));
            Assert.Equal("bar", config.GetString("root.unquoted-string"));
            Assert.Equal("foo bar", config.GetString("root.concat-string"));
            Assert.True(
                new[] {1, 2, 3, 4}.SequenceEqual(ConfigurationFactory.ParseString(hocon).GetIntList("root.array")));
            Assert.True(
                new[] {1, 2, 3, 4}.SequenceEqual(
                    ConfigurationFactory.ParseString(hocon).GetIntList("root.array-newline-element")));
            Assert.True(
                new[] {"1 2 3 4"}.SequenceEqual(
                    ConfigurationFactory.ParseString(hocon).GetStringList("root.array-single-element")));
        }

        [Fact]
        public void CanParseJson()
        {
            string hocon = @"
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
            Config config = ConfigurationFactory.ParseString(hocon);
            Assert.Equal("1", config.GetString("root.int"));
            Assert.Equal("1.23", config.GetString("root.double"));
            Assert.Equal(true, config.GetBoolean("root.bool"));
            Assert.Equal(true, config.GetBoolean("root.object.hasContent"));
            Assert.Equal(null, config.GetString("root.null"));
            Assert.Equal("foo", config.GetString("root.string"));
            Assert.True(new[] {1, 2, 3}.SequenceEqual(ConfigurationFactory.ParseString(hocon).GetIntList("root.array")));
        }

        [Fact]
        public void CanMergeObject()
        {
            string hocon = @"
a.b.c = {
        x = 1
        y = 2
    }
a.b.c = {
        z = 3
    }
";
            Config config = ConfigurationFactory.ParseString(hocon);
            Assert.Equal("1", config.GetString("a.b.c.x"));
            Assert.Equal("2", config.GetString("a.b.c.y"));
            Assert.Equal("3", config.GetString("a.b.c.z"));
        }

        [Fact]
        public void CanOverrideObject()
        {
            string hocon = @"
a.b = 1
a = null
a.c = 3
";
            Config config = ConfigurationFactory.ParseString(hocon);
            Assert.Equal(null, config.GetString("a.b"));
            Assert.Equal("3", config.GetString("a.c"));
        }

        [Fact]
        public void CanParseObject()
        {
            string hocon = @"
a {
  b = 1
}
";
            Assert.Equal("1", ConfigurationFactory.ParseString(hocon).GetString("a.b"));
        }

        [Fact]
        public void CanTrimValue()
        {
            string hocon = "a= \t \t 1 \t \t,";
            Assert.Equal("1", ConfigurationFactory.ParseString(hocon).GetString("a"));
        }

        [Fact]
        public void CanTrimConcatenatedValue()
        {
            string hocon = "a= \t \t 1 2 3 \t \t,";
            Assert.Equal("1 2 3", ConfigurationFactory.ParseString(hocon).GetString("a"));
        }

        [Fact]
        public void CanConsumeCommaAfterValue()
        {
            string hocon = "a=1,";
            Assert.Equal("1", ConfigurationFactory.ParseString(hocon).GetString("a"));
        }

        [Fact]
        public void CanAssignIpAddressToField()
        {
            string hocon = @"a=127.0.0.1";
            Assert.Equal("127.0.0.1", ConfigurationFactory.ParseString(hocon).GetString("a"));
        }

        [Fact]
        public void CanAssignConcatenatedValueToField()
        {
            string hocon = @"a=1 2 3";
            Assert.Equal("1 2 3", ConfigurationFactory.ParseString(hocon).GetString("a"));
        }

        [Fact]
        public void CanAssignValueToQuotedField()
        {
            string hocon = @"""a""=1";
            Assert.Equal(1L, ConfigurationFactory.ParseString(hocon).GetLong("a"));
        }

        [Fact]
        public void CanAssignValueToPathExpression()
        {
            string hocon = @"a.b.c=1";
            Assert.Equal(1L, ConfigurationFactory.ParseString(hocon).GetLong("a.b.c"));
        }

        [Fact]
        public void CanAssignValuesToPathExpressions()
        {
            string hocon = @"
a.b.c=1
a.b.d=2
a.b.e.f=3
";
            Config config = ConfigurationFactory.ParseString(hocon);
            Assert.Equal(1L, config.GetLong("a.b.c"));
            Assert.Equal(2L, config.GetLong("a.b.d"));
            Assert.Equal(3L, config.GetLong("a.b.e.f"));
        }

        [Fact]
        public void CanAssignLongToField()
        {
            string hocon = @"a=1";
            Assert.Equal(1L, ConfigurationFactory.ParseString(hocon).GetLong("a"));
        }

        [Fact]
        public void CanAssignArrayToField()
        {
            string hocon = @"a=
[
    1
    2
    3
]";
            Assert.True(new[] {1, 2, 3}.SequenceEqual(ConfigurationFactory.ParseString(hocon).GetIntList("a")));

            //hocon = @"a= [ 1, 2, 3 ]";
            //Assert.True(new[] { 1, 2, 3 }.SequenceEqual(ConfigurationFactory.ParseString(hocon).GetIntList("a")));
        }

        [Fact]
        public void CanConcatenateArray()
        {
            string hocon = @"a=[1,2] [3,4]";
            Assert.True(new[] {1, 2, 3, 4}.SequenceEqual(ConfigurationFactory.ParseString(hocon).GetIntList("a")));
        }

        [Fact]
        public void CanAssignSubstitutionToField()
        {
            string hocon = @"a{
    b = 1
    c = ${a.b}
    d = ${a.c}23
}";
            Assert.Equal(1, ConfigurationFactory.ParseString(hocon).GetInt("a.c"));
            Assert.Equal(123, ConfigurationFactory.ParseString(hocon).GetInt("a.d"));
        }

        [Fact]
        public void CanAssignDoubleToField()
        {
            string hocon = @"a=1.1";
            Assert.Equal(1.1, ConfigurationFactory.ParseString(hocon).GetDouble("a"));
        }

        [Fact]
        public void CanAssignNullToField()
        {
            string hocon = @"a=null";
            Assert.Null(ConfigurationFactory.ParseString(hocon).GetString("a"));
        }

        [Fact]
        public void CanAssignBooleanToField()
        {
            string hocon = @"a=true";
            Assert.Equal(true, ConfigurationFactory.ParseString(hocon).GetBoolean("a"));
            hocon = @"a=false";
            Assert.Equal(false, ConfigurationFactory.ParseString(hocon).GetBoolean("a"));

            hocon = @"a=on";
            Assert.Equal(true, ConfigurationFactory.ParseString(hocon).GetBoolean("a"));
            hocon = @"a=off";
            Assert.Equal(false, ConfigurationFactory.ParseString(hocon).GetBoolean("a"));
        }

        [Fact]
        public void CanAssignQuotedStringToField()
        {
            string hocon = @"a=""hello""";
            Assert.Equal("hello", ConfigurationFactory.ParseString(hocon).GetString("a"));
        }

        [Fact]
        public void CanAssignUnQuotedStringToField()
        {
            string hocon = @"a=hello";
            Assert.Equal("hello", ConfigurationFactory.ParseString(hocon).GetString("a"));
        }

        [Fact]
        public void CanAssignTrippleQuotedStringToField()
        {
            string hocon = @"a=""""""hello""""""";
            Assert.Equal("hello", ConfigurationFactory.ParseString(hocon).GetString("a"));
        }

        [Fact]
        public void CanUseFallback()
        {
            string hocon1 = @"
foo {
   bar {
      a=123
   }
}";
            string hocon2 = @"
foo {
   bar {
      a=1
      b=2
      c=3
   }
}";

            Config config1 = ConfigurationFactory.ParseString(hocon1);
            Config config2 = ConfigurationFactory.ParseString(hocon2);

            Config config = config1.WithFallback(config2);

            Assert.Equal(123, config.GetInt("foo.bar.a"));
            Assert.Equal(2, config.GetInt("foo.bar.b"));
            Assert.Equal(3, config.GetInt("foo.bar.c"));
        }

        [Fact]
        public void CanUseFallbackInSubConfig()
        {
            string hocon1 = @"
foo {
   bar {
      a=123
   }
}";
            string hocon2 = @"
foo {
   bar {
      a=1
      b=2
      c=3
   }
}";

            Config config1 = ConfigurationFactory.ParseString(hocon1);
            Config config2 = ConfigurationFactory.ParseString(hocon2);

            Config config = config1.WithFallback(config2).GetConfig("foo.bar");

            Assert.Equal(123, config.GetInt("a"));
            Assert.Equal(2, config.GetInt("b"));
            Assert.Equal(3, config.GetInt("c"));
        }

        [Fact]
        public void CanUseMultiLevelFallback()
        {
            string hocon1 = @"
foo {
   bar {
      a=123
   }
}";
            string hocon2 = @"
foo {
   bar {
      a=1
      b=2
      c=3
   }
}";
            string hocon3 = @"
foo {
   bar {
      a=99
      zork=555
   }
}";
            string hocon4 = @"
foo {
   bar {
      borkbork=-1
   }
}";

            Config config1 = ConfigurationFactory.ParseString(hocon1);
            Config config2 = ConfigurationFactory.ParseString(hocon2);
            Config config3 = ConfigurationFactory.ParseString(hocon3);
            Config config4 = ConfigurationFactory.ParseString(hocon4);

            Config config = config1.WithFallback(config2.WithFallback(config3.WithFallback(config4)));

            config.GetInt("foo.bar.a").ShouldBe(123);
            config.GetInt("foo.bar.b").ShouldBe(2);
            config.GetInt("foo.bar.c").ShouldBe(3);
            config.GetInt("foo.bar.zork").ShouldBe(555);
            config.GetInt("foo.bar.borkbork").ShouldBe(-1);
        }

        [Fact]
        public void CanUseFluentMultiLevelFallback()
        {
            string hocon1 = @"
foo {
   bar {
      a=123
   }
}";
            string hocon2 = @"
foo {
   bar {
      a=1
      b=2
      c=3
   }
}";
            string hocon3 = @"
foo {
   bar {
      a=99
      zork=555
   }
}";
            string hocon4 = @"
foo {
   bar {
      borkbork=-1
   }
}";

            Config config1 = ConfigurationFactory.ParseString(hocon1);
            Config config2 = ConfigurationFactory.ParseString(hocon2);
            Config config3 = ConfigurationFactory.ParseString(hocon3);
            Config config4 = ConfigurationFactory.ParseString(hocon4);

            Config config = config1.WithFallback(config2).WithFallback(config3).WithFallback(config4);

            config.GetInt("foo.bar.a").ShouldBe(123);
            config.GetInt("foo.bar.b").ShouldBe(2);
            config.GetInt("foo.bar.c").ShouldBe(3);
            config.GetInt("foo.bar.zork").ShouldBe(555);
            config.GetInt("foo.bar.borkbork").ShouldBe(-1);
        }
    }
}