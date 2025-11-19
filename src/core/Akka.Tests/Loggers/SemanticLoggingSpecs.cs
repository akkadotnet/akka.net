//-----------------------------------------------------------------------
// <copyright file="SemanticLoggingSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Event;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Loggers
{
    public class SemanticLoggingSpecs : AkkaSpec
    {
        [Fact(DisplayName = "MessageTemplateParser should parse positional templates correctly")]
        public void MessageTemplateParser_should_parse_positional_templates()
        {
            var template = "Value is {0} and status {1}";
            var propertyNames = MessageTemplateParser.GetPropertyNames(template);

            propertyNames.Should().NotBeNull();
            propertyNames.Should().HaveCount(2);
            propertyNames[0].Should().Be("0");
            propertyNames[1].Should().Be("1");
        }

        [Fact(DisplayName = "MessageTemplateParser should parse named templates correctly")]
        public void MessageTemplateParser_should_parse_named_templates()
        {
            var template = "User {UserId} logged in from {IpAddress}";
            var propertyNames = MessageTemplateParser.GetPropertyNames(template);

            propertyNames.Should().NotBeNull();
            propertyNames.Should().HaveCount(2);
            propertyNames[0].Should().Be("UserId");
            propertyNames[1].Should().Be("IpAddress");
        }

        [Fact(DisplayName = "MessageTemplateParser should handle escaped braces")]
        public void MessageTemplateParser_should_handle_escaped_braces()
        {
            var template = "Use {{braces}} for {Value}";
            var propertyNames = MessageTemplateParser.GetPropertyNames(template);

            propertyNames.Should().NotBeNull();
            propertyNames.Should().HaveCount(1);
            propertyNames[0].Should().Be("Value");
        }

        [Fact(DisplayName = "MessageTemplateParser should handle format specifiers")]
        public void MessageTemplateParser_should_handle_format_specifiers()
        {
            var template = "Value is {Amount:N2} dollars";
            var propertyNames = MessageTemplateParser.GetPropertyNames(template);

            propertyNames.Should().NotBeNull();
            propertyNames.Should().HaveCount(1);
            propertyNames[0].Should().Be("Amount");
        }

        [Fact(DisplayName = "MessageTemplateParser should handle alignment specifiers")]
        public void MessageTemplateParser_should_handle_alignment_specifiers()
        {
            var template = "Name: {Name,10} Age: {Age,-5}";
            var propertyNames = MessageTemplateParser.GetPropertyNames(template);

            propertyNames.Should().NotBeNull();
            propertyNames.Should().HaveCount(2);
            propertyNames[0].Should().Be("Name");
            propertyNames[1].Should().Be("Age");
        }

        [Fact(DisplayName = "MessageTemplateParser should return empty list for no placeholders")]
        public void MessageTemplateParser_should_return_empty_for_no_placeholders()
        {
            var template = "This is a plain message";
            var propertyNames = MessageTemplateParser.GetPropertyNames(template);

            propertyNames.Should().NotBeNull();
            propertyNames.Should().BeEmpty();
        }

        [Fact(DisplayName = "MessageTemplateParser should handle malformed templates gracefully")]
        public void MessageTemplateParser_should_handle_malformed_templates()
        {
            var template = "Value is {0 and {1} without closing";
            var propertyNames = MessageTemplateParser.GetPropertyNames(template);

            // Should not throw, parses "0 and {1" from the first {..} pair
            propertyNames.Should().NotBeNull();
            propertyNames.Should().HaveCount(1);
            propertyNames[0].Should().Be("0 and {1");
        }

        [Fact(DisplayName = "MessageTemplateParser should cache parsed templates")]
        public void MessageTemplateParser_should_cache_parsed_templates()
        {
            var template = "User {UserId} logged in";

            // First call - cache miss
            var result1 = MessageTemplateParser.GetPropertyNames(template);

            // Second call - should hit cache (same reference)
            var result2 = MessageTemplateParser.GetPropertyNames(template);

            result1.Should().BeSameAs(result2, "cached results should return the same instance");
        }

        [Fact(DisplayName = "LogMessage should extract property names correctly")]
        public void LogMessage_should_extract_property_names()
        {
            var formatter = DefaultLogMessageFormatter.Instance;
            var logMessage = new DefaultLogMessage(formatter, "User {UserId} logged in", 123);

            var propertyNames = logMessage.PropertyNames;

            propertyNames.Should().HaveCount(1);
            propertyNames[0].Should().Be("UserId");
        }

        [Fact(DisplayName = "LogMessage should create property dictionary correctly")]
        public void LogMessage_should_create_property_dictionary()
        {
            var formatter = DefaultLogMessageFormatter.Instance;
            var logMessage = new DefaultLogMessage(formatter, "User {UserId} from {IpAddress}", 123, "192.168.1.1");

            var properties = logMessage.GetProperties();

            properties.Should().HaveCount(2);
            properties["UserId"].Should().Be(123);
            properties["IpAddress"].Should().Be("192.168.1.1");
        }

        [Fact(DisplayName = "LogMessage should handle mismatched property counts")]
        public void LogMessage_should_handle_mismatched_property_counts()
        {
            var formatter = DefaultLogMessageFormatter.Instance;

            // More values than properties
            var logMessage1 = new DefaultLogMessage(formatter, "User {UserId}", 123, "extra");
            var properties1 = logMessage1.GetProperties();
            properties1.Should().HaveCount(1);
            properties1["UserId"].Should().Be(123);

            // More properties than values
            var logMessage2 = new DefaultLogMessage(formatter, "User {UserId} from {IpAddress}");
            var properties2 = logMessage2.GetProperties();
            properties2.Should().BeEmpty();
        }

        [Fact(DisplayName = "LogMessage property dictionary should be cached")]
        public void LogMessage_property_dictionary_should_be_cached()
        {
            var formatter = DefaultLogMessageFormatter.Instance;
            var logMessage = new DefaultLogMessage(formatter, "User {UserId}", 123);

            var properties1 = logMessage.GetProperties();
            var properties2 = logMessage.GetProperties();

            properties1.Should().BeSameAs(properties2, "cached properties should return the same instance");
        }

        [Fact(DisplayName = "SemanticLogMessageFormatter should format positional templates")]
        public void SemanticLogMessageFormatter_should_format_positional_templates()
        {
            var formatter = SemanticLogMessageFormatter.Instance;
            var result = formatter.Format("Value is {0} and status {1}", 42, "OK");

            result.Should().Be("Value is 42 and status OK");
        }

        [Fact(DisplayName = "SemanticLogMessageFormatter should format named templates")]
        public void SemanticLogMessageFormatter_should_format_named_templates()
        {
            var formatter = SemanticLogMessageFormatter.Instance;
            var result = formatter.Format("User {UserId} logged in from {IpAddress}", 123, "192.168.1.1");

            result.Should().Be("User 123 logged in from 192.168.1.1");
        }

        [Fact(DisplayName = "SemanticLogMessageFormatter should handle format specifiers")]
        public void SemanticLogMessageFormatter_should_handle_format_specifiers()
        {
            var formatter = SemanticLogMessageFormatter.Instance;
            var result = formatter.Format("Amount: {Amount:N2}", 1234.5);

            // Handle culture differences - just check it contains the number with decimals
            result.Should().MatchRegex(@"Amount: \d[,\d]*\.50");
        }

        [Fact(DisplayName = "SemanticLogMessageFormatter should handle missing arguments")]
        public void SemanticLogMessageFormatter_should_handle_missing_arguments()
        {
            var formatter = SemanticLogMessageFormatter.Instance;
            var result = formatter.Format("User {UserId} from {IpAddress}", 123);

            result.Should().Contain("123");
            result.Should().Contain("{IpAddress}"); // Missing arg stays as placeholder
        }

        [Fact(DisplayName = "SemanticLogMessageFormatter should handle null values")]
        public void SemanticLogMessageFormatter_should_handle_null_values()
        {
            var formatter = SemanticLogMessageFormatter.Instance;
            var result = formatter.Format("Value is {Value}", (object)null);

            result.Should().Be("Value is null");
        }

        [Fact(DisplayName = "LogEventExtensions.TryGetProperties should work with LogMessage")]
        public void LogEventExtensions_TryGetProperties_should_work_with_LogMessage()
        {
            var formatter = SemanticLogMessageFormatter.Instance;
            var logMessage = new DefaultLogMessage(formatter, "User {UserId}", 123);
            var logEvent = new Info(null, "TestSource", typeof(SemanticLoggingSpecs), logMessage);

            var result = logEvent.TryGetProperties(out var properties);

            result.Should().BeTrue();
            properties.Should().NotBeNull();
            properties["UserId"].Should().Be(123);
        }

        [Fact(DisplayName = "LogEventExtensions.TryGetProperties should return false for string messages")]
        public void LogEventExtensions_TryGetProperties_should_return_false_for_strings()
        {
            var logEvent = new Info(null, "TestSource", typeof(SemanticLoggingSpecs), "Plain string message");

            var result = logEvent.TryGetProperties(out var properties);

            result.Should().BeFalse();
            properties.Should().BeNull();
        }

        [Fact(DisplayName = "LogEventExtensions.GetPropertyNames should work with LogMessage")]
        public void LogEventExtensions_GetPropertyNames_should_work()
        {
            var formatter = SemanticLogMessageFormatter.Instance;
            var logMessage = new DefaultLogMessage(formatter, "User {UserId} from {IpAddress}", 123, "192.168.1.1");
            var logEvent = new Info(null, "TestSource", typeof(SemanticLoggingSpecs), logMessage);

            var propertyNames = logEvent.GetPropertyNames();

            propertyNames.Should().HaveCount(2);
            propertyNames.Should().Contain("UserId");
            propertyNames.Should().Contain("IpAddress");
        }

        [Fact(DisplayName = "LogEventExtensions.GetTemplate should extract format string")]
        public void LogEventExtensions_GetTemplate_should_work()
        {
            var formatter = SemanticLogMessageFormatter.Instance;
            var logMessage = new DefaultLogMessage(formatter, "User {UserId}", 123);
            var logEvent = new Info(null, "TestSource", typeof(SemanticLoggingSpecs), logMessage);

            var template = logEvent.GetTemplate();

            template.Should().Be("User {UserId}");
        }

        [Fact(DisplayName = "LogEventExtensions.GetParameters should extract values")]
        public void LogEventExtensions_GetParameters_should_work()
        {
            var formatter = SemanticLogMessageFormatter.Instance;
            var logMessage = new DefaultLogMessage(formatter, "User {UserId} from {IpAddress}", 123, "192.168.1.1");
            var logEvent = new Info(null, "TestSource", typeof(SemanticLoggingSpecs), logMessage);

            var parameters = logEvent.GetParameters().ToArray();

            parameters.Should().HaveCount(2);
            parameters[0].Should().Be(123);
            parameters[1].Should().Be("192.168.1.1");
        }

        [Fact(DisplayName = "LruCache should evict oldest entries when full")]
        public void LruCache_should_evict_oldest_entries()
        {
            var cache = new LruCache<int, string>(3);

            cache.Add(1, "one");
            cache.Add(2, "two");
            cache.Add(3, "three");
            cache.Add(4, "four"); // Should evict 1

            cache.TryGet(1, out _).Should().BeFalse("1 should have been evicted");
            cache.TryGet(2, out var val2).Should().BeTrue();
            val2.Should().Be("two");
        }

        [Fact(DisplayName = "LruCache should promote accessed entries")]
        public void LruCache_should_promote_accessed_entries()
        {
            var cache = new LruCache<int, string>(3);

            cache.Add(1, "one");
            cache.Add(2, "two");
            cache.Add(3, "three");

            // Access 1, promoting it to front
            cache.TryGet(1, out _).Should().BeTrue();

            // Add 4, should evict 2 (oldest)
            cache.Add(4, "four");

            cache.TryGet(1, out _).Should().BeTrue("1 was promoted");
            cache.TryGet(2, out _).Should().BeFalse("2 should have been evicted");
            cache.TryGet(3, out _).Should().BeTrue();
            cache.TryGet(4, out _).Should().BeTrue();
        }

        [Fact(DisplayName = "End-to-end semantic logging should work")]
        public void End_to_end_semantic_logging_should_work()
        {
            var formatter = SemanticLogMessageFormatter.Instance;
            var logMessage = new DefaultLogMessage(formatter, "User {UserId} performed action {Action}", 123, "Login");
            var logEvent = new Info(null, "TestSource", typeof(SemanticLoggingSpecs), logMessage);

            // Property extraction
            logEvent.TryGetProperties(out var properties).Should().BeTrue();
            properties["UserId"].Should().Be(123);
            properties["Action"].Should().Be("Login");

            // Template extraction
            logEvent.GetTemplate().Should().Be("User {UserId} performed action {Action}");

            // Message formatting
            logMessage.ToString().Should().Be("User 123 performed action Login");
        }
    }
}
