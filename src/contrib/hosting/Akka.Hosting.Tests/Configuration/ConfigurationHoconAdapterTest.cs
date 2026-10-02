// -----------------------------------------------------------------------
//  <copyright file="ConfigurationHoconAdapterTest.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Configuration;
using Akka.Hosting.Configuration;
using FluentAssertions;
using FluentAssertions.Extensions;
using Microsoft.Extensions.Configuration;
using Xunit;
using static FluentAssertions.FluentActions;

namespace Akka.Hosting.Tests.Configuration;

public class ConfigurationHoconAdapterTest: IAsyncLifetime
{
    private const string ConfigSource = @"{
  ""akka"": {
    ""actor.serialization-bindings"" : {
        ""\""System.Int32\"""": ""json""
    },
    ""cluster"": {
      ""roles"": [ ""front-end"", ""back-end"" ],
      ""role"" : {
        ""back-end"" : 5
      },
      ""app-version"": ""1.0.0"",
      ""min-nr-of-members"": 99,
      ""seed-nodes"": [ ""akka.tcp://system@somewhere.com:9999"" ],
      ""log-info"": false,
      ""log-info-verbose"": true
    }
  },
  ""test1"": ""test1 content"",
  ""test2.a"": ""on"",
  ""test2.b.c"": ""2s"",
  ""test2.b.d"": ""test2.b.d content"",
  ""test2.d"": ""test2.d content"",
  ""test3"": ""3"",
  ""test4"": 4
}";

    private readonly IConfigurationRoot _root;

    public ConfigurationHoconAdapterTest()
    {
        Environment.SetEnvironmentVariable("akka__TEST_VALUE_1__A", "A VALUE");
        Environment.SetEnvironmentVariable("akka__TEST_VALUE_1__B", "B VALUE");
        Environment.SetEnvironmentVariable("akka__TEST_VALUE_1__C__D", "D");
        Environment.SetEnvironmentVariable("akka__TEST_VALUE_2__0", "ZERO");
        Environment.SetEnvironmentVariable("akka__TEST_VALUE_2__22", "TWO");
        Environment.SetEnvironmentVariable("akka__TEST_VALUE_2__1", "ONE");
        
        Environment.SetEnvironmentVariable("akka__test_value_3__a", "a value");
        Environment.SetEnvironmentVariable("akka__test_value_3__b", "b value");
        Environment.SetEnvironmentVariable("akka__test_value_3__c__d", "d");
        Environment.SetEnvironmentVariable("akka__test_value_4__0", "zero");
        Environment.SetEnvironmentVariable("akka__test_value_4__22", "two");
        Environment.SetEnvironmentVariable("akka__test_value_4__1", "one");
        Environment.SetEnvironmentVariable("akka__actor__serialization_bindings2__\"System.Object\"", "hyperion");
        
        // Issue #631
        // Double underscore cases
        Environment.SetEnvironmentVariable("__MISE_SESSION", "some-random-string");
        // Quadruple underscore cases
        Environment.SetEnvironmentVariable("MISE____SESSION", "some-random-string");
        Environment.SetEnvironmentVariable("____MISE_SESSION", "some-random-string");
        
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(ConfigSource));
        _root = new ConfigurationBuilder()
            .AddJsonStream(stream)
            .AddEnvironmentVariables()
            .Build();
    }

    public ValueTask InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Environment.SetEnvironmentVariable("akka__TEST_VALUE_1__A", null);
        Environment.SetEnvironmentVariable("akka__TEST_VALUE_1__B", null);
        Environment.SetEnvironmentVariable("akka__TEST_VALUE_1__C__D", null);
        Environment.SetEnvironmentVariable("akka__TEST_VALUE_2__0", null);
        Environment.SetEnvironmentVariable("akka__TEST_VALUE_2__22", null);
        Environment.SetEnvironmentVariable("akka__TEST_VALUE_2__1", null);
        
        Environment.SetEnvironmentVariable("akka__test_value_3__a", null);
        Environment.SetEnvironmentVariable("akka__test_value_3__b", null);
        Environment.SetEnvironmentVariable("akka__test_value_3__c__d", null);
        Environment.SetEnvironmentVariable("akka__test_value_4__0", null);
        Environment.SetEnvironmentVariable("akka__test_value_4__22", null);
        Environment.SetEnvironmentVariable("akka__test_value_4__1", null);
        Environment.SetEnvironmentVariable("akka__actor__serialization_bindings2__\"System.Object\"", null);
        
        Environment.SetEnvironmentVariable("__MISE_SESSION", null);
        Environment.SetEnvironmentVariable("MISE____SESSION", null);
        Environment.SetEnvironmentVariable("____MISE_SESSION", null);
        
        return ValueTask.CompletedTask;
    }
    
    #region Adapter unit tests

    [Fact(DisplayName = "Normalized adaptor should read environment variable sourced configuration correctly")]
    public void EnvironmentVariableTest()
    {
        var config = _root.ToHocon();
        // should be normalized
        config.GetString("akka.test-value-1.a").Should().Be("A VALUE");
        config.GetString("akka.TEST-VALUE-1.A").Should().BeNull();
        
        // should be normalized
        config.GetString("akka.test-value-1.b").Should().Be("B VALUE");
        config.GetString("akka.TEST-VALUE-1.B").Should().BeNull();
        
        // should be normalized
        config.GetString("akka.test-value-1.c.d").Should().Be("D");
        config.GetString("akka.TEST-VALUE-1.C.D").Should().BeNull();

        // should be normalized
        var array = config.GetStringList("akka.test-value-2");
        array[0].Should().Be("ZERO");
        array[1].Should().Be("ONE");
        array[2].Should().Be("TWO");
        config.GetStringList("AKKA.TEST-VALUE-2").Should().BeEmpty();

        // proper cased environment vars should read just fine
        config.GetString("akka.test-value-3.a").Should().Be("a value");
        config.GetString("akka.test-value-3.b").Should().Be("b value");
        config.GetString("akka.test-value-3.c.d").Should().Be("d");
        array = config.GetStringList("akka.test-value-4");
        array[0].Should().Be("zero");
        array[1].Should().Be("one");
        array[2].Should().Be("two");

        // edge case should also be normalized and not usable
        var bindings = config.GetConfig("akka.actor.serialization-bindings2").AsEnumerable()
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        bindings.ContainsKey("System.Object").Should().BeFalse();
        bindings.ContainsKey("system.object").Should().BeTrue();
        bindings["system.object"].GetString().Should().Be("hyperion");
        
        // empty string keyed objects should be accessible by using "__"
        config.GetString("__.mise-session").Should().Be("some-random-string");
        config.GetString("mise.__.session").Should().Be("some-random-string");
        config.GetString("__.__.mise-session").Should().Be("some-random-string");
    }

    [Fact(DisplayName = "Non-normalized adaptor should read environment variable sourced configuration correctly")]
    public void EnvironmentVariableCaseSensitiveTest()
    {
        var config = _root.ToHocon(false);
        
        // should not be normalized
        config.GetString("akka.TEST-VALUE-1.A").Should().Be("A VALUE");
        config.GetString("akka.test-value-1.a").Should().BeNull();
        
        // should not be normalized
        config.GetString("akka.TEST-VALUE-1.B").Should().Be("B VALUE");
        config.GetString("akka.test-value-1.b").Should().BeNull();
        
        // should not be normalized
        config.GetString("akka.TEST-VALUE-1.C.D").Should().Be("D");
        config.GetString("akka.test-value-1.c.d").Should().BeNull();

        // should not be normalized
        config.GetStringList("akka.test-value-2").Should().BeEmpty();
        var array = config.GetStringList("akka.TEST-VALUE-2");
        array[0].Should().Be("ZERO");
        array[1].Should().Be("ONE");
        array[2].Should().Be("TWO");

        // proper cased environment vars should read just fine
        config.GetString("akka.test-value-3.a").Should().Be("a value");
        config.GetString("akka.test-value-3.b").Should().Be("b value");
        config.GetString("akka.test-value-3.c.d").Should().Be("d");
        array = config.GetStringList("akka.test-value-4");
        array[0].Should().Be("zero");
        array[1].Should().Be("one");
        array[2].Should().Be("two");

        // edge case should not be normalized and usable
        var bindings = config.GetConfig("akka.actor.serialization-bindings2").AsEnumerable()
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        bindings.ContainsKey("System.Object").Should().BeTrue();
        bindings["System.Object"].GetString().Should().Be("hyperion");
        
        // empty string keyed objects should be accessible by using "__"
        config.GetString("__.MISE-SESSION").Should().Be("some-random-string");
        config.GetString("MISE.__.SESSION").Should().Be("some-random-string");
        config.GetString("__.__.MISE-SESSION").Should().Be("some-random-string");
    }

    [Fact(DisplayName = "Non-normalized Adaptor should read quote enclosed key inside JSON settings correctly")]
    public void NonNormalizedJsonQuotedKeyTest()
    {
        var config = _root.ToHocon(false);
        var bindings = config.GetConfig("akka.actor.serialization-bindings").AsEnumerable()
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        bindings.ContainsKey("System.Int32").Should().BeTrue();
        bindings["System.Int32"].GetString().Should().Be("json");
    }
    
    [Fact(DisplayName = "Normalized Adaptor should read quote enclosed key inside JSON settings incorrectly")]
    public void NormalizedJsonQuotedKeyTest()
    {
        var config = _root.ToHocon();
        var bindings = config.GetConfig("akka.actor.serialization-bindings").AsEnumerable()
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        
        bindings.ContainsKey("System.Int32").Should().BeFalse();
        bindings.ContainsKey("system.int32").Should().BeTrue();
        bindings["system.int32"].GetString().Should().Be("json");
    }
    
    [Fact(DisplayName = "Adaptor should expand keys")]
    public void EncodedKeyTest()
    {
        var config = _root.ToHocon();
        var test2 = config.GetConfig("test2");
        test2.Should().NotBeNull();
        test2.GetBoolean("a").Should().BeTrue();
        test2.GetTimeSpan("b.c").Should().Be(2.Seconds());
        test2.GetString("b.d").Should().Be("test2.b.d content");
        test2.GetString("d").Should().Be("test2.d content");
    }

    [Fact(DisplayName = "Adaptor should convert correctly")]
    public void ArrayTest()
    {
        var config = _root.ToHocon();
        config.GetString("test1").Should().Be("test1 content");
        config.GetInt("test3").Should().Be(3);
        config.GetInt("test4").Should().Be(4);
        
        config.GetStringList("akka.cluster.roles").Should().BeEquivalentTo("front-end", "back-end");
        config.GetInt("akka.cluster.role.back-end").Should().Be(5);
        config.GetString("akka.cluster.app-version").Should().Be("1.0.0");
        config.GetInt("akka.cluster.min-nr-of-members").Should().Be(99);
        config.GetStringList("akka.cluster.seed-nodes").Should()
            .BeEquivalentTo("akka.tcp://system@somewhere.com:9999");
        config.GetBoolean("akka.cluster.log-info").Should().BeFalse();
        config.GetBoolean("akka.cluster.log-info-verbose").Should().BeTrue();
    }

    #endregion

    #region ToHocon unit tests

    [Fact(DisplayName = "Regression test: connection-string should convert to hocon properly")]
    public void ConnectionStringShouldConvertToHoconProperly()
    {
        const string connectionString = "Server=COMPUTER1\\TEST;Database=BV_PROD_1;uid=**;pwd=--;TransparentNetworkIPResolution=False;Connection Timeout=180;Max Pool Size=120;Column Encryption Setting=Enabled;";
        var hoconString = $"connection-string = {connectionString.ToHocon()}";
        Config? cfg = null;
        Invoking(() => cfg = ConfigurationFactory.ParseString(hoconString))
            .Should().NotThrow();
        
        cfg!.GetString("connection-string").Should().Be(connectionString);
    }
    
    [MemberData(nameof(IllegalCharacterGenerator))]
    [Theory(DisplayName = "ToHocon(string) should add quotes to string with illegal characters")]
    public void StringToHoconQuote(string c)
    {
        var testString = $"this is {c} a test";
        var hoconized = testString.ToHocon();

        switch (c)
        {
            case "\\":
                // backslash is a special case
                hoconized.Should().Be("\"this is \\\\ a test\"");
                break;
            case "\"":
                // quote is a special case
                hoconized.Should().Be("\"this is \\\" a test\"");
                break;
            default:
                hoconized.Should().Be($"\"{testString}\"");
                break;
        }
        
        var hoconString = $"test-string = {hoconized}";
        Config? cfg = null;
        Invoking(() => cfg = ConfigurationFactory.ParseString(hoconString))
            .Should().NotThrow();
        
        cfg!.GetString("test-string").Should().Be(testString);
    }

    [MemberData(nameof(EscapeCharacterGenerator))]
    [Theory(DisplayName = "ToHocon(string) should add backslash escape character on escapable characters")]
    public void StringToHoconEscape(string escape, string expected)
    {
        var hoconSafe = escape.ToHocon();
        hoconSafe.Should().Be(expected);
        
        var hoconString = $"test-string = {$"a test {escape} string".ToHocon()}";
        Config? cfg = null;
        Invoking(() => cfg = ConfigurationFactory.ParseString(hoconString))
            .Should().NotThrow();
        cfg!.GetString("test-string").Should().Be($"a test {escape} string");
    }

    public static IEnumerable<object[]> IllegalCharacterGenerator()
    {
        const string illegals = "$\"{}[]:=,#`^?!@*&\\";
        foreach (var c in illegals)
        {
            yield return new [] { (object) $"{c}" };
        }
    }
    
    public static IEnumerable<object[]> EscapeCharacterGenerator()
    {
        yield return new[] { (object)"\\", "\"\\\\\"" };
        yield return new[] { (object)"\"", "\"\\\"\"" };
        yield return new[] { (object)"/", "\"\\/\"" };
        yield return new[] { (object)"\b", "\"\\b\"" };
        yield return new[] { (object)"\f", "\"\\f\"" };
        yield return new[] { (object)"\n", "\"\\n\"" };
        yield return new[] { (object)"\r", "\"\\r\"" };
        yield return new[] { (object)"\t", "\"\\t\"" };
        yield return new[] { (object)"\uFB2F", "\"\\ufb2f\"" };
    }
    
    #endregion
}