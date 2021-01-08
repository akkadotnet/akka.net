//-----------------------------------------------------------------------
// <copyright file="HoconTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Configuration;
using Akka.Configuration.Hocon;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Configuration
{
    public class HoconTests
    {
        //Added tests to conform to the HOCON spec https://github.com/Lightbendhub/config/blob/master/HOCON.md
        [Fact]
        public void Can_use_paths_as_keys_3_14()
        {
            var hocon1 = @"3.14 : 42";
            var hocon2 = @"3 { 14 : 42}";
            Assert.Equal(ConfigurationFactory.ParseString(hocon1).GetString("3.14"),
                ConfigurationFactory.ParseString(hocon2).GetString("3.14"));
        }

        [Fact]
        public void Can_use_paths_as_keys_3()
        {
            var hocon1 = @"3 : 42";
            var hocon2 = @"""3"" : 42";
            Assert.Equal(ConfigurationFactory.ParseString(hocon1).GetString("3"),
                ConfigurationFactory.ParseString(hocon2).GetString("3"));
        }

        [Fact]
        public void Can_use_paths_as_keys_true()
        {
            var hocon1 = @"true : 42";
            var hocon2 = @"""true"" : 42";
            Assert.Equal(ConfigurationFactory.ParseString(hocon1).GetString("true"),
                ConfigurationFactory.ParseString(hocon2).GetString("true"));
        }

        [Fact]
        public void Can_use_paths_as_keys_FooBar()
        {
            var hocon1 = @"foo.bar : 42";
            var hocon2 = @"foo { bar : 42 }";
            Assert.Equal(ConfigurationFactory.ParseString(hocon1).GetString("foo.bar"),
                ConfigurationFactory.ParseString(hocon2).GetString("foo.bar"));
        }

        [Fact]
        public void Can_use_paths_as_keys_FooBarBaz()
        {
            var hocon1 = @"foo.bar.baz : 42";
            var hocon2 = @"foo { bar { baz : 42 } }";
            Assert.Equal(ConfigurationFactory.ParseString(hocon1).GetString("foo.bar.baz"),
                ConfigurationFactory.ParseString(hocon2).GetString("foo.bar.baz"));
        }

        [Fact]
        public void Can_use_paths_as_keys_AX_AY()
        {
            var hocon1 = @"a.x : 42, a.y : 43";
            var hocon2 = @"a { x : 42, y : 43 }";
            Assert.Equal(ConfigurationFactory.ParseString(hocon1).GetString("a.x"),
                ConfigurationFactory.ParseString(hocon2).GetString("a.x"));
            Assert.Equal(ConfigurationFactory.ParseString(hocon1).GetString("a.y"),
                ConfigurationFactory.ParseString(hocon2).GetString("a.y"));
        }

        [Fact]
        public void Can_use_paths_as_keys_A_B_C()
        {
            var hocon1 = @"a b c : 42";
            var hocon2 = @"""a b c"" : 42";
            Assert.Equal(ConfigurationFactory.ParseString(hocon1).GetString("a b c"),
                ConfigurationFactory.ParseString(hocon2).GetString("a b c"));
        }


        [Fact]
        public void Can_concatenate_substituted_unquoted_string()
        {
            var hocon = @"a {
  name = Roger
  c = Hello my name is ${a.name}
}";
            Assert.Equal("Hello my name is Roger", ConfigurationFactory.ParseString(hocon).GetString("a.c"));
        }

        [Fact]
        public void Can_concatenate_substituted_array()
        {
            var hocon = @"a {
  b = [1,2,3]
  c = ${a.b} [4,5,6]
}";
            Assert.True(new[] {1, 2, 3, 4, 5, 6}.SequenceEqual(ConfigurationFactory.ParseString(hocon).GetIntList("a.c")));
        }

        [Fact]
        public void Can_parse_sub_config()
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
            Assert.Equal(1, subConfig.GetInt("b.c"));
            Assert.Equal(true, subConfig.GetBoolean("b.d"));
        }


        [Fact]
        public void Can_parse_hocon()
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
        public void Can_parse_json()
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
            Assert.Equal("1", config.GetString("root.int"));
            Assert.Equal("1.23", config.GetString("root.double"));
            Assert.Equal(true, config.GetBoolean("root.bool"));
            Assert.Equal(true, config.GetBoolean("root.object.hasContent"));
            Assert.Equal(null, config.GetString("root.null"));
            Assert.Equal("foo", config.GetString("root.string"));
            Assert.True(new[] {1, 2, 3}.SequenceEqual(ConfigurationFactory.ParseString(hocon).GetIntList("root.array")));
        }

        [Fact]
        public void Can_merge_object()
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
            Assert.Equal("1", config.GetString("a.b.c.x"));
            Assert.Equal("2", config.GetString("a.b.c.y"));
            Assert.Equal("3", config.GetString("a.b.c.z"));
        }

        [Fact]
        public void Can_override_object()
        {
            var hocon = @"
a.b = 1
a = null
a.c = 3
";
            var config = ConfigurationFactory.ParseString(hocon);
            Assert.Equal(null, config.GetString("a.b"));
            Assert.Equal("3", config.GetString("a.c"));
        }

        [Fact]
        public void Can_parse_object()
        {
            var hocon = @"
a {
  b = 1
}
";
            Assert.Equal("1", ConfigurationFactory.ParseString(hocon).GetString("a.b"));
        }

        [Fact]
        public void Can_trim_value()
        {
            var hocon = "a= \t \t 1 \t \t,";
            Assert.Equal("1", ConfigurationFactory.ParseString(hocon).GetString("a"));
        }

        [Fact]
        public void Can_trim_concatenated_value()
        {
            var hocon = "a= \t \t 1 2 3 \t \t,";
            Assert.Equal("1 2 3", ConfigurationFactory.ParseString(hocon).GetString("a"));
        }

        [Fact]
        public void Can_consume_comma_after_value()
        {
            var hocon = "a=1,";
            Assert.Equal("1", ConfigurationFactory.ParseString(hocon).GetString("a"));
        }

        [Fact]
        public void Can_assign_ipaddress_to_field()
        {
            var hocon = @"a=127.0.0.1";
            Assert.Equal("127.0.0.1", ConfigurationFactory.ParseString(hocon).GetString("a"));
        }

        [Fact]
        public void Can_assign_concatenated_value_to_field()
        {
            var hocon = @"a=1 2 3";
            Assert.Equal("1 2 3", ConfigurationFactory.ParseString(hocon).GetString("a"));
        }

        [Fact]
        public void Can_assign_value_to_quoted_field()
        {
            var hocon = @"""a""=1";
            Assert.Equal(1L, ConfigurationFactory.ParseString(hocon).GetLong("a"));
        }

        [Fact]
        public void Can_assign_value_to_path_expression()
        {
            var hocon = @"a.b.c=1";
            Assert.Equal(1L, ConfigurationFactory.ParseString(hocon).GetLong("a.b.c"));
        }

        [Fact]
        public void Can_assign_values_to_path_expressions()
        {
            var hocon = @"
a.b.c=1
a.b.d=2
a.b.e.f=3
";
            var config = ConfigurationFactory.ParseString(hocon);
            Assert.Equal(1L, config.GetLong("a.b.c"));
            Assert.Equal(2L, config.GetLong("a.b.d"));
            Assert.Equal(3L, config.GetLong("a.b.e.f"));
        }

        [Fact]
        public void Can_assign_long_to_field()
        {
            var hocon = @"a=1";
            Assert.Equal(1L, ConfigurationFactory.ParseString(hocon).GetLong("a"));
        }

        [Fact]
        public void Can_assign_array_to_field()
        {
            var hocon = @"a=
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
        public void Can_concatenate_array()
        {
            var hocon = @"a=[1,2] [3,4]";
            Assert.True(new[] {1, 2, 3, 4}.SequenceEqual(ConfigurationFactory.ParseString(hocon).GetIntList("a")));
        }

        [Fact]
        public void Can_assign_substitution_to_field()
        {
            var hocon = @"a{
    b = 1
    c = ${a.b}
    d = ${a.c}23
}";
            Assert.Equal(1, ConfigurationFactory.ParseString(hocon).GetInt("a.c"));
            Assert.Equal(123, ConfigurationFactory.ParseString(hocon).GetInt("a.d"));
        }

        [Fact]
        public void Can_assign_double_to_field()
        {
            var hocon = @"a=1.1";
            Assert.Equal(1.1, ConfigurationFactory.ParseString(hocon).GetDouble("a"));
        }

        [Fact]
        public void Can_assign_null_to_field()
        {
            var hocon = @"a=null";
            Assert.Null(ConfigurationFactory.ParseString(hocon).GetString("a"));
        }

        [Fact]
        public void Can_assign_boolean_to_field()
        {
            var hocon = @"a=true";
            Assert.Equal(true, ConfigurationFactory.ParseString(hocon).GetBoolean("a"));
            hocon = @"a=false";
            Assert.Equal(false, ConfigurationFactory.ParseString(hocon).GetBoolean("a"));

            hocon = @"a=on";
            Assert.Equal(true, ConfigurationFactory.ParseString(hocon).GetBoolean("a"));
            hocon = @"a=off";
            Assert.Equal(false, ConfigurationFactory.ParseString(hocon).GetBoolean("a"));
        }

        [Fact]
        public void Can_assign_quoted_string_to_field()
        {
            var hocon = @"a=""hello""";
            Assert.Equal("hello", ConfigurationFactory.ParseString(hocon).GetString("a"));
        }

        [Fact]
        public void Can_assign_un_quoted_string_to_field()
        {
            var hocon = @"a=hello";
            Assert.Equal("hello", ConfigurationFactory.ParseString(hocon).GetString("a"));
        }

        [Fact]
        public void Can_assign_triple_quoted_string_to_field()
        {
            var hocon = @"a=""""""hello""""""";
            Assert.Equal("hello", ConfigurationFactory.ParseString(hocon).GetString("a"));
        }

        [Fact]
        public void Can_assign_triple_quoted_string_with_unescaped_chars_to_field()
        {
            var hocon = @"a=""""""hello\y\o\u""""""";
            Assert.Equal("hello\\y\\o\\u", ConfigurationFactory.ParseString(hocon).GetString("a"));
        }

        [Fact]
        public void Can_assign_unescaped_path_like_variable_to_field()
        {
            var hocon = @"a=""""""C:\Dev\somepath\to\a\file.txt""""""";
            Assert.Equal("C:\\Dev\\somepath\\to\\a\\file.txt", ConfigurationFactory.ParseString(hocon).GetString("a"));
        }
        
        [Fact]
        public void Can_use_fallback()
        {
            var hocon1 = @"
foo {
   bar {
      a=123
   }
}";
            var hocon2 = @"
foo {
   bar {
      a=1
      b=2
      c=3
   }
}";

            var config1 = ConfigurationFactory.ParseString(hocon1);
            var config2 = ConfigurationFactory.ParseString(hocon2);

            var config = config1.WithFallback(config2);

            Assert.Equal(123, config.GetInt("foo.bar.a"));
            Assert.Equal(2, config.GetInt("foo.bar.b"));
            Assert.Equal(3, config.GetInt("foo.bar.c"));
        }

        [Fact]
        public void Can_use_fallback_in_sub_config()
        {
            var hocon1 = @"
foo {
   bar {
      a=123
   }
}";
            var hocon2 = @"
foo {
   bar {
      a=1
      b=2
      c=3
   }
}";

            var config1 = ConfigurationFactory.ParseString(hocon1);
            var config2 = ConfigurationFactory.ParseString(hocon2);

            var config = config1.WithFallback(config2).GetConfig("foo.bar");

            Assert.Equal(123, config.GetInt("a"));
            Assert.Equal(2, config.GetInt("b"));
            Assert.Equal(3, config.GetInt("c"));
        }

        [Fact]
        public void Can_use_multi_level_fallback()
        {
            var hocon1 = @"
foo {
   bar {
      a=123
   }
}";
            var hocon2 = @"
foo {
   bar {
      a=1
      b=2
      c=3
   }
}";
            var hocon3 = @"
foo {
   bar {
      a=99
      zork=555
   }
}";
            var hocon4 = @"
foo {
   bar {
      borkbork=-1
   }
}";

            var config1 = ConfigurationFactory.ParseString(hocon1);
            var config2 = ConfigurationFactory.ParseString(hocon2);
            var config3 = ConfigurationFactory.ParseString(hocon3);
            var config4 = ConfigurationFactory.ParseString(hocon4);

            var config = config1.WithFallback(config2.WithFallback(config3.WithFallback(config4)));

            config.GetInt("foo.bar.a").ShouldBe(123);
            config.GetInt("foo.bar.b").ShouldBe(2);
            config.GetInt("foo.bar.c").ShouldBe(3);
            config.GetInt("foo.bar.zork").ShouldBe(555);
            config.GetInt("foo.bar.borkbork").ShouldBe(-1);
        }

        [Fact]
        public void Can_use_fluent_multi_level_fallback()
        {
            var hocon1 = @"
foo {
   bar {
      a=123
   }
}";
            var hocon2 = @"
foo {
   bar {
      a=1
      b=2
      c=3
   }
}";
            var hocon3 = @"
foo {
   bar {
      a=99
      zork=555
   }
}";
            var hocon4 = @"
foo {
   bar {
      borkbork=-1
   }
}";

            var config1 = ConfigurationFactory.ParseString(hocon1);
            var config2 = ConfigurationFactory.ParseString(hocon2);
            var config3 = ConfigurationFactory.ParseString(hocon3);
            var config4 = ConfigurationFactory.ParseString(hocon4);

            var config = config1.WithFallback(config2).WithFallback(config3).WithFallback(config4);

            config.GetInt("foo.bar.a").ShouldBe(123);
            config.GetInt("foo.bar.b").ShouldBe(2);
            config.GetInt("foo.bar.c").ShouldBe(3);
            config.GetInt("foo.bar.zork").ShouldBe(555);
            config.GetInt("foo.bar.borkbork").ShouldBe(-1);
        }

        [Fact(DisplayName = @"Scalar value should not be overriden by an object in fallback and vice versa")]
        public void Scalar_value_should_not_be_overriden_by_an_object_in_fallback_and_vice_versa() {
            var hocon1 = @"
a {
    b = 1
}
";
            var hocon2 = @"
a {
    b {
        c = 2
    }
}
";

            var config1 = ConfigurationFactory.ParseString(hocon1);
            var config2 = ConfigurationFactory.ParseString(hocon2);

            var config12 = config1.WithFallback(config2);
            var config21 = config2.WithFallback(config1);

            Assert.Equal(1, config12.GetInt("a.b"));
            Assert.Equal(2, config21.GetInt("a.b.c"));
        }

        [Fact(DisplayName = "Config constructed from merging with fallback should not share state with origin")]
        public void Config_constructed_from_merging_with_fallback_should_not_share_state_with_origin()
        {
            var hocon1 = @"
a {
    b = 1
}
";
            var hocon2 = @"
a {
    c = 2
}
";

            var config1 = ConfigurationFactory.ParseString(hocon1);
            var config2 = ConfigurationFactory.ParseString(hocon2);

            var config12 = config1.WithFallback(config2);

            Assert.Equal(2, config12.GetInt("a.c"));
            Assert.Equal(0, config1.GetInt("a.c"));
            Assert.Equal(0, config2.GetInt("a.b"));
        }

        [Fact]
        public void Can_parse_quoted_keys()
        {
            var hocon = @"
a {
   ""some quoted, key"": 123
}
";
            var config = ConfigurationFactory.ParseString(hocon);
            config.GetInt("a.some quoted, key").ShouldBe(123);
        }

        [Fact]
        public void Can_parse_quoted_keys_with_dots()
        {
            var hocon = @"
a {
   ""/abc/d.ev/*"": 123
}
";
            var config = ConfigurationFactory.ParseString(hocon);
            config.GetConfig("a").Root.GetObject().GetKey("/abc/d.ev/*").GetInt().ShouldBe(123);
        }

        [Fact]
        public void Get_config_supports_quoting()
        {
            var hocon = @"
a {
   ""/abc/d.ev/*"": 123
}
";
            var config = ConfigurationFactory.ParseString(hocon);
            config.GetConfig("a").GetConfig(@"""/abc/d.ev/*""").ShouldNotBe(null);
        }

        [Fact]
        public void Get_config_supports_quoting_combined_with_dotting()
        {
            var hocon = @"
a {
   ""/abc/d.ev/*"".d: 123
}
";
            var config = ConfigurationFactory.ParseString(hocon);
            config.GetConfig(@"a.""/abc/d.ev/*""").ShouldNotBe(null);
            config.GetConfig(@"a.""/abc/d.ev/*"".d").ShouldNotBe(null);
        }

        [Fact]
        public void Can_enumerate_quoted_keys()
        {
            var hocon = @"
a {
   ""some quoted, key"": 123
}
";
            var config = ConfigurationFactory.ParseString(hocon);
            var config2 = config.GetConfig("a");
            var enumerable = config2.AsEnumerable();

            enumerable.Select(kvp => kvp.Key).First().ShouldBe("some quoted, key");
        }

        [Fact]
        public void Can_enumerate_quoted_keys_with_dots()
        {
            var hocon = @"
a {
   ""/abc/d.ev/*"": 123
}
";
            var config = ConfigurationFactory.ParseString(hocon);
            var config2 = config.GetConfig("a");
            var enumerable = config2.AsEnumerable();

            enumerable.Select(kvp => kvp.Key).First().ShouldBe("/abc/d.ev/*");
        }

        [Fact]
        public void Can_parse_serializers_and_bindings()
        {
            var hocon = @"
akka.actor {
    serializers {
      akka-containers = ""Akka.Remote.Serialization.MessageContainerSerializer, Akka.Remote""
      proto = ""Akka.Remote.Serialization.ProtobufSerializer, Akka.Remote""
      daemon-create = ""Akka.Remote.Serialization.DaemonMsgCreateSerializer, Akka.Remote""
    }

    serialization-bindings {
      # Since com.google.protobuf.Message does not extend Serializable but
      # GeneratedMessage does, need to use the more specific one here in order
      # to avoid ambiguity
      ""Akka.Actor.ActorSelectionMessage"" = akka-containers
      ""Akka.Remote.DaemonMsgCreate, Akka.Remote"" = daemon-create
    }

}";

            var config = ConfigurationFactory.ParseString(hocon);

            var serializersConfig = config.GetConfig("akka.actor.serializers").AsEnumerable().ToList();
            var serializerBindingConfig = config.GetConfig("akka.actor.serialization-bindings").AsEnumerable().ToList();

            serializersConfig.Select(kvp => kvp.Value)
                .First()
                .GetString()
                .ShouldBe("Akka.Remote.Serialization.MessageContainerSerializer, Akka.Remote");
            serializerBindingConfig.Select(kvp => kvp.Key).Last().ShouldBe("Akka.Remote.DaemonMsgCreate, Akka.Remote");
        }

        [Fact]
        public void Can_overwrite_value()
        {
            var hocon = @"
test {
  value  = 123
}
test.value = 456
";
            var config = ConfigurationFactory.ParseString(hocon);
            config.GetInt("test.value").ShouldBe(456);
        }

        [Fact]
        public void Can_substitute_object()
        {
            var hocon = @"a {
  b {
      foo = hello
      bar = 123
  }
  c {
     d = xyz
     e = ${a.b}
  }  
}";
            var ace = ConfigurationFactory.ParseString(hocon).GetConfig("a.c.e");
            Assert.Equal("hello", ace.GetString("foo"));
            Assert.Equal(123, ace.GetInt("bar"));
        }

        [Fact]
        public void Can_assign_null_string_to_field()
        {
            var hocon = @"a=null";
            Assert.Equal(null, ConfigurationFactory.ParseString(hocon).GetString("a"));
        }

        [Fact(Skip = "we currently do not make any distinction between quoted and unquoted strings once parsed")]
        public void Can_assign_quoted_null_string_to_field()
        {
            var hocon = @"a=""null""";
            Assert.Equal("null", ConfigurationFactory.ParseString(hocon).GetString("a"));
        }

        [Fact]
        public void Can_parse_include()
        {
            var hocon = @"a {
  b { 
       include ""foo""
  }";
            var includeHocon = @"
x = 123
y = hello
";
            Func<string, HoconRoot> include = s => Parser.Parse(includeHocon, null);
            var config = ConfigurationFactory.ParseString(hocon,include);

            Assert.Equal(123,config.GetInt("a.b.x"));
            Assert.Equal("hello", config.GetString("a.b.y"));
        }

        [Fact]
        public void Can_resolve_substitutes_in_include()
        {
            var hocon = @"a {
  b { 
       include ""foo""
  }";
            var includeHocon = @"
x = 123
y = ${x}
";
            Func<string, HoconRoot> include = s => Parser.Parse(includeHocon, null);
            var config = ConfigurationFactory.ParseString(hocon, include);

            Assert.Equal(123, config.GetInt("a.b.x"));
            Assert.Equal(123, config.GetInt("a.b.y"));
        }

        [Fact]
        public void Can_resolve_substitutes_in_nested_includes()
        {
            var hocon = @"a.b.c {
  d { 
       include ""foo""
  }";
            var includeHocon = @"
f = 123
e {
      include ""foo""
}
";

            var includeHocon2 = @"
x = 123
y = ${x}
";

            Func<string, HoconRoot> include2 = s => Parser.Parse(includeHocon2, null);
            Func<string, HoconRoot> include = s => Parser.Parse(includeHocon, include2);
            var config = ConfigurationFactory.ParseString(hocon, include);

            Assert.Equal(123, config.GetInt("a.b.c.d.e.x"));
            Assert.Equal(123, config.GetInt("a.b.c.d.e.y"));
        }

        [Fact]
        public void Can_parse_unquoted_ipv4()
        {
            var hocon = @"
ip = 127.0.0.1
";
            Assert.Equal("127.0.0.1", ConfigurationFactory.ParseString(hocon).GetString("ip"));
        }

        [Fact]
        public void Can_parse_quoted_ipv4()
        {
            var hocon = @"
ip = ""127.0.0.1""
";
            Assert.Equal("127.0.0.1", ConfigurationFactory.ParseString(hocon).GetString("ip"));
        }

        [Fact(Skip = "Not allowed according to current HOCON spec")]
        public void Can_parse_unquoted_ipv6()
        {
            var hocon = @"
ip = ::1
";
            var res = ConfigurationFactory.ParseString(hocon).GetString("ip");
            Assert.Equal("::1", res);
        }

        [Fact]
        public void Can_parse_quoted_ipv6()
        {
            var hocon = @"
ip = ""::1""
";
            var res = ConfigurationFactory.ParseString(hocon).GetString("ip");
            Assert.Equal("::1", res);
        }

        [Fact]
        public void Can_parse_non_abbreviated_timespan()
        {
            var hocon = "timespan = 10 seconds";

            var res = ConfigurationFactory.ParseString(hocon).GetTimeSpan("timespan");
            Assert.Equal(10, res.TotalSeconds);
        }

        [Fact]
        public void Can_parse_abbreviated_timespan() 
        {
            var hocon = "timespan = 10 s";

            var res = ConfigurationFactory.ParseString(hocon).GetTimeSpan("timespan");
            Assert.Equal(10, res.TotalSeconds);
        }

        [Fact]
        public void Can_parse_abbreviated_timespan2()
        {
            var hocon = "timespan = 0.05 s";

            var res = ConfigurationFactory.ParseString(hocon).GetTimeSpan("timespan");
            Assert.Equal(50, res.TotalMilliseconds);
        }

        [Fact(Skip = "not working yet")]
        public void Can_substitute_with_concated_string()
        {
            var hocon = @"
    akka.cluster.name = cluster
    akka.cluster.seed-node = ""akka.tcp://${akka.cluster.name}@127.0.0.1:4053""";

            var config = ConfigurationFactory.ParseString(hocon);
            var actual = config.GetString("akka.cluster.seed-node");
            Console.Out.WriteLine($"RESULT:{actual}");
            Assert.Equal("akka.tcp://cluster@127.0.0.1:4053", actual);
        }

        [Fact]
        public void Can_parse_unquoted_string_list()
        {
            var hocon = @"hocon-array = [array-value-1, array-value-2]";
            var config = ConfigurationFactory.ParseString(hocon);
            var actual = config.GetStringList("hocon-array");
            Assert.True(actual.Contains("array-value-1"));
            Assert.True(actual.Contains("array-value-2"));
        }

        [Fact]
        public void Should_throw_an_exception_when_parsing_invalid_unquoted_string()
        {
            var hocon = @"unquoted-string = akka.tcp://Cluster@127.0.0.1:4053";
            Assert.Throws<ConfigurationException>(() => { ConfigurationFactory.ParseString(hocon); });
        }
        
        [Fact]
        public void Should_throw_an_exception_when_parsing_invalid_unquoted_string_inside_array()
        {
            var hocon = @"akka.cluster.seed-nodes = [akka.tcp://Cluster@127.0.0.1:4053]";
            Assert.Throws<ConfigurationException>(() => { ConfigurationFactory.ParseString(hocon); });
        }
    }
}

