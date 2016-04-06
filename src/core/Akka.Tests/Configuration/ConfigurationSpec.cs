//-----------------------------------------------------------------------
// <copyright file="ConfigurationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Configuration.Hocon;
using System.Configuration;
using System.Linq;
using Akka.Configuration;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Configuration
{
    public class ConfigurationSpec : AkkaSpec
    {
        [Fact]
        public void Deserializes_hocon_configuration_from_net_config_file()
        {
            var section = (AkkaConfigurationSection)ConfigurationManager.GetSection("akka");
            Assert.NotNull(section);
            Assert.False(string.IsNullOrEmpty(section.Hocon.Content));
            var akkaConfig = section.AkkaConfig;
            Assert.NotNull(akkaConfig);
        }

        [Fact]
        public void Can_create_config_from_source_object()
        {
            var source = new MyObjectConfig
            {
                StringProperty = "aaa",
                BoolProperty = true,
                IntergerArray = new[]{1,2,3,4 }
            };

            var config = ConfigurationFactory.FromObject(source);

            Assert.Equal("aaa", config.GetString("StringProperty"));
            Assert.Equal(true, config.GetBoolean("BoolProperty"));

            Assert.Equal(new[] { 1, 2, 3, 4 }, config.GetIntList("IntergerArray").ToArray());
        }

        [Fact]
        public void Can_merge_objects()
        {
            var hocon1 = @"
a {
    b = 123
    c = 456
    d = 789
    sub {
        aa = 123
    }
}
";

            var hocon2 = @"
a {
    c = 999
    e = 888
    sub {
        bb = 456
    }
}
";

            var root1 = Parser.Parse(hocon1,null);
            var root2 = Parser.Parse(hocon2, null);

            var obj1 = root1.Value.GetObject();
            var obj2 = root2.Value.GetObject();
            obj1.Merge(obj2);

            var config = new Config(root1);

            Assert.Equal(123, config.GetInt("a.b"));
            Assert.Equal(456, config.GetInt("a.c"));
            Assert.Equal(789, config.GetInt("a.d"));
            Assert.Equal(888, config.GetInt("a.e"));
            Assert.Equal(888, config.GetInt("a.e"));
            Assert.Equal(123, config.GetInt("a.sub.aa"));
            Assert.Equal(456, config.GetInt("a.sub.bb"));

        }

        public class MyObjectConfig
        {
            public string StringProperty { get; set; }
            public bool BoolProperty { get; set; }
            public int[] IntergerArray { get; set; }
        }

        [Fact]
        public void Parsing_empty_string_should_produce_empty_hocon_root()
        {
            var value = Parser.Parse(string.Empty, null).Value;
            value.IsEmpty.ShouldBeTrue();
        }

        [Fact]
        public void Config_Empty_is_Empty()
        {
            ConfigurationFactory.Empty.IsEmpty.ShouldBeTrue();
        }
   }
}

