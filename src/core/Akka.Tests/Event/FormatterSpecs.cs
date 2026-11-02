//-----------------------------------------------------------------------
// <copyright file="FormatterSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Event;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Event
{
    public class FormatterSpecs
    {
        [Fact(DisplayName = "SemanticLogMessageFormatter should return diagnostic for mismatched positional args")]
        public void SemanticLogMessageFormatter_should_return_diagnostic_for_mismatched_positional_args()
        {
            var formatter = SemanticLogMessageFormatter.Instance;
            var result = formatter.Format("{0} {1} {2}", new object[] { "a", "b" });

            result.Should().StartWith("[INVALID LOG FORMAT]");
            result.Should().Contain("{0} {1} {2}");
            result.Should().Contain("a");
            result.Should().Contain("b");
        }

        [Fact(DisplayName = "SemanticLogMessageFormatter should return diagnostic for mismatched positional args via IReadOnlyList path")]
        public void SemanticLogMessageFormatter_should_return_diagnostic_for_mismatched_positional_args_via_readonly_list()
        {
            var formatter = SemanticLogMessageFormatter.Instance;
            var args = new LogValues<string, string>("a", "b");
            var result = formatter.Format("{0} {1} {2}", args);

            result.Should().StartWith("[INVALID LOG FORMAT]");
            result.Should().Contain("{0} {1} {2}");
        }

        [Fact(DisplayName = "DefaultLogMessageFormatter should return diagnostic for mismatched positional args")]
        public void DefaultLogMessageFormatter_should_return_diagnostic_for_mismatched_positional_args()
        {
            var formatter = DefaultLogMessageFormatter.Instance;
            var result = formatter.Format("{0} {1} {2}", new object[] { "a", "b" });

            result.Should().StartWith("[INVALID LOG FORMAT]");
            result.Should().Contain("{0} {1} {2}");
            result.Should().Contain("a");
            result.Should().Contain("b");
        }

        [Fact(DisplayName = "DefaultLogMessageFormatter should return diagnostic for mismatched positional args via IEnumerable overload")]
        public void DefaultLogMessageFormatter_should_return_diagnostic_for_mismatched_positional_args_via_enumerable()
        {
            var formatter = DefaultLogMessageFormatter.Instance;
            var result = formatter.Format("{0} {1} {2}", new object[] { "a", "b" });

            result.Should().StartWith("[INVALID LOG FORMAT]");
            result.Should().Contain("{0} {1} {2}");
        }

        [Fact(DisplayName = "LogFilterEvaluator should handle FormatException gracefully")]
        public void LogFilterEvaluator_should_handle_FormatException_gracefully()
        {
            // Use the empty evaluator (no filters) - this is the path third-party loggers hit
            var evaluator = LogFilterEvaluator.NoFilters;

            // Create a log event with a bad format string
            var badMessage = new DefaultLogMessage(
                SemanticLogMessageFormatter.Instance,
                "{0} {1} {2}",
                "a", "b");

            var evt = new Warning("test-source", typeof(FormatterSpecs), badMessage);

            var result = evaluator.ShouldTryKeepMessage(evt, out var expandedMessage);

            result.Should().BeTrue();
            expandedMessage.Should().NotBeNullOrEmpty();
            expandedMessage.Should().Contain("[INVALID LOG FORMAT]");
        }

        [Fact(DisplayName = "LogFilterEvaluator with content filters should handle FormatException gracefully")]
        public void LogFilterEvaluator_with_content_filters_should_handle_FormatException_gracefully()
        {
            // Create an evaluator with a content filter to exercise the non-empty filter path
            var filter = new RegexLogMessageFilter(
                new System.Text.RegularExpressions.Regex("never_match_anything"));
            var evaluator = new LogFilterEvaluator(new LogFilterBase[] { filter });

            var badMessage = new DefaultLogMessage(
                SemanticLogMessageFormatter.Instance,
                "{0} {1} {2}",
                "a", "b");

            var evt = new Warning("test-source", typeof(FormatterSpecs), badMessage);

            var result = evaluator.ShouldTryKeepMessage(evt, out var expandedMessage);

            result.Should().BeTrue();
            expandedMessage.Should().Contain("[INVALID LOG FORMAT]");
        }

        [Fact(DisplayName = "SemanticLogMessageFormatter should still format valid positional templates correctly")]
        public void SemanticLogMessageFormatter_should_still_format_valid_positional_templates()
        {
            var formatter = SemanticLogMessageFormatter.Instance;
            var result = formatter.Format("{0} and {1}", new object[] { "hello", "world" });
            result.Should().Be("hello and world");
        }

        [Fact(DisplayName = "DefaultLogMessageFormatter should still format valid positional templates correctly")]
        public void DefaultLogMessageFormatter_should_still_format_valid_positional_templates()
        {
            var formatter = DefaultLogMessageFormatter.Instance;
            var result = formatter.Format("{0} and {1}", new object[] { "hello", "world" });
            result.Should().Be("hello and world");
        }
    }
}
