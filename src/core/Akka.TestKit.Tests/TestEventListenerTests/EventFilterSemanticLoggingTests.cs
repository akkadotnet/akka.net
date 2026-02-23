//-----------------------------------------------------------------------
// <copyright file="EventFilterSemanticLoggingTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Event;
using FluentAssertions;
using Xunit;

namespace Akka.TestKit.Tests.TestEventListenerTests
{
    /// <summary>
    /// Tests that EventFilter correctly matches semantic log messages (parametrized templates).
    /// Reproduces the customer scenario where converting from string interpolation to
    /// semantic logging templates broke EventFilter assertions.
    /// </summary>
    public class EventFilterSemanticLoggingTests : EventFilterTestBase
    {
        public EventFilterSemanticLoggingTests() : base("akka.loglevel=INFO")
        {
        }

        protected override void SendRawLogEventMessage(object message)
        {
            Sys.EventStream.Publish(new Info(GetType().FullName, GetType(), message));
        }

        private void PublishSemanticInfo(LogMessage logMessage, string source = null)
        {
            Sys.EventStream.Publish(new Info(source ?? GetType().FullName, GetType(), logMessage));
        }

        #region Exact Match (message:)

        [Fact(DisplayName = "EventFilter message: should match formatted semantic log output")]
        public async Task Message_exact_match_formatted_output()
        {
            var logMessage = new DefaultLogMessage(
                SemanticLogMessageFormatter.Instance,
                "User {UserId} logged in",
                12345);

            // EventFilter with message: matches the fully formatted string
            await EventFilter.Info(message: "User 12345 logged in").ExpectOneAsync(() =>
            {
                PublishSemanticInfo(logMessage);
                return Task.CompletedTask;
            });
            TestSuccessful = true;
        }

        [Fact(DisplayName = "EventFilter message: should match template string directly")]
        public async Task Message_exact_match_template_string()
        {
            var logMessage = new DefaultLogMessage(
                SemanticLogMessageFormatter.Instance,
                "User {UserId} logged in",
                12345);

            // EventFilter also tries matching the template format itself
            await EventFilter.Info(message: "User {UserId} logged in").ExpectOneAsync(() =>
            {
                PublishSemanticInfo(logMessage);
                return Task.CompletedTask;
            });
            TestSuccessful = true;
        }

        [Fact(DisplayName = "EventFilter message: should not match when neither template nor formatted output matches")]
        public async Task Message_exact_no_match()
        {
            var logMessage = new DefaultLogMessage(
                SemanticLogMessageFormatter.Instance,
                "User {UserId} logged in",
                12345);

            // This filter should NOT match - let the message through
            await EventFilter.Info(message: "Order processed").ExpectOneAsync(() =>
            {
                PublishSemanticInfo(logMessage); // should pass through (not intercepted)
                Sys.EventStream.Publish(new Info(GetType().FullName, GetType(),
                    "Order processed")); // this one matches
                return Task.CompletedTask;
            });
            // The unmatched semantic message should arrive at TestActor
            await ExpectMsgAsync<Info>(e => e.Message is LogMessage lm &&
                                            lm.ToString() == "User 12345 logged in");
            TestSuccessful = true;
        }

        #endregion

        #region Partial Matchers (contains: / start:)

        [Fact(DisplayName = "EventFilter contains: should match against formatted output")]
        public async Task Contains_matches_formatted_output()
        {
            var logMessage = new DefaultLogMessage(
                SemanticLogMessageFormatter.Instance,
                "User {UserId} logged in from {IpAddress}",
                42, "192.168.1.1");

            await EventFilter.Info(contains: "192.168.1.1").ExpectOneAsync(() =>
            {
                PublishSemanticInfo(logMessage);
                return Task.CompletedTask;
            });
            TestSuccessful = true;
        }

        [Fact(DisplayName = "EventFilter contains: should match template placeholder names")]
        public async Task Contains_matches_template_placeholder()
        {
            var logMessage = new DefaultLogMessage(
                SemanticLogMessageFormatter.Instance,
                "User {UserId} logged in",
                42);

            // Matches against the template string which contains "{UserId}"
            await EventFilter.Info(contains: "UserId").ExpectOneAsync(() =>
            {
                PublishSemanticInfo(logMessage);
                return Task.CompletedTask;
            });
            TestSuccessful = true;
        }

        [Fact(DisplayName = "EventFilter start: should match beginning of formatted output")]
        public async Task Start_matches_formatted_output()
        {
            var logMessage = new DefaultLogMessage(
                SemanticLogMessageFormatter.Instance,
                "Order {OrderId} placed by {Customer}",
                999, "Alice");

            await EventFilter.Info(start: "Order 999").ExpectOneAsync(() =>
            {
                PublishSemanticInfo(logMessage);
                return Task.CompletedTask;
            });
            TestSuccessful = true;
        }

        [Fact(DisplayName = "EventFilter start: should match beginning of template string")]
        public async Task Start_matches_template_string()
        {
            var logMessage = new DefaultLogMessage(
                SemanticLogMessageFormatter.Instance,
                "Order {OrderId} placed by {Customer}",
                999, "Alice");

            // Template string starts with "Order {OrderId}"
            await EventFilter.Info(start: "Order {Order").ExpectOneAsync(() =>
            {
                PublishSemanticInfo(logMessage);
                return Task.CompletedTask;
            });
            TestSuccessful = true;
        }

        #endregion

        #region Null Handling

        [Fact(DisplayName = "Named template renders null as 'null' string")]
        public async Task Named_template_null_renders_as_null_string()
        {
            var logMessage = new DefaultLogMessage(
                SemanticLogMessageFormatter.Instance,
                "Value is {Value}",
                new object[] { null });

            // The formatted output is "Value is null"
            await EventFilter.Info(contains: "null").ExpectOneAsync(() =>
            {
                PublishSemanticInfo(logMessage);
                return Task.CompletedTask;
            });
            TestSuccessful = true;
        }

        [Fact(DisplayName = "Named template: EventFilter message: matches full formatted null")]
        public async Task Named_template_null_exact_match()
        {
            var logMessage = new DefaultLogMessage(
                SemanticLogMessageFormatter.Instance,
                "Bonus: {Amount}",
                new object[] { null });

            await EventFilter.Info(message: "Bonus: null").ExpectOneAsync(() =>
            {
                PublishSemanticInfo(logMessage);
                return Task.CompletedTask;
            });
            TestSuccessful = true;
        }

        #endregion

        #region Type Variety

        [Fact(DisplayName = "EventFilter matches semantic log with int argument")]
        public async Task Type_int()
        {
            var logMessage = new DefaultLogMessage(
                SemanticLogMessageFormatter.Instance,
                "Count: {Count}", 42);

            await EventFilter.Info(message: "Count: 42").ExpectOneAsync(() =>
            {
                PublishSemanticInfo(logMessage);
                return Task.CompletedTask;
            });
            TestSuccessful = true;
        }

        [Fact(DisplayName = "EventFilter matches semantic log with decimal preserving trailing zeros")]
        public async Task Type_decimal_trailing_zeros()
        {
            var logMessage = new DefaultLogMessage(
                SemanticLogMessageFormatter.Instance,
                "Amount: {Amount}", 100.0m);

            // decimal preserves trailing zeros: 100.0m.ToString() == "100.0"
            await EventFilter.Info(message: "Amount: 100.0").ExpectOneAsync(() =>
            {
                PublishSemanticInfo(logMessage);
                return Task.CompletedTask;
            });
            TestSuccessful = true;
        }

        [Fact(DisplayName = "EventFilter matches semantic log with nullable decimal null")]
        public async Task Type_nullable_decimal_null()
        {
            decimal? value = null;
            var logMessage = new DefaultLogMessage(
                SemanticLogMessageFormatter.Instance,
                "Returns: {TotalReturns}",
                new object[] { value });

            await EventFilter.Info(message: "Returns: null").ExpectOneAsync(() =>
            {
                PublishSemanticInfo(logMessage);
                return Task.CompletedTask;
            });
            TestSuccessful = true;
        }

        [Fact(DisplayName = "EventFilter matches semantic log with bool")]
        public async Task Type_bool()
        {
            var logMessage = new DefaultLogMessage(
                SemanticLogMessageFormatter.Instance,
                "Active: {IsActive}", true);

            await EventFilter.Info(message: "Active: True").ExpectOneAsync(() =>
            {
                PublishSemanticInfo(logMessage);
                return Task.CompletedTask;
            });
            TestSuccessful = true;
        }

        [Fact(DisplayName = "EventFilter matches semantic log with enum")]
        public async Task Type_enum()
        {
            var logMessage = new DefaultLogMessage(
                SemanticLogMessageFormatter.Instance,
                "Level: {Level}", LogLevel.WarningLevel);

            await EventFilter.Info(message: "Level: WarningLevel").ExpectOneAsync(() =>
            {
                PublishSemanticInfo(logMessage);
                return Task.CompletedTask;
            });
            TestSuccessful = true;
        }

        #endregion

        #region Customer Scenario Reproduction

        [Fact(DisplayName = "Old positional format with :f specifier - original working code")]
        public async Task Customer_scenario_old_positional_format()
        {
            // Original working code used positional templates with :f format specifier:
            // Logger.Info("{0} Bet containing {1} constituents - BetId={2}, PlayerId={3}, Amount={4:f}, BonusAmount={5:f}", ...)
            var logMessage = new DefaultLogMessage(
                SemanticLogMessageFormatter.Instance,
                "{0} Bet containing {1} constituents - BetId={2}, PlayerId={3}, Amount={4:f}, BonusAmount={5:f}",
                "Win", 3, "bet-123", "player-456", 100.0m, 25.50m);

            // With positional templates, string.Format applies the :f specifier
            var expected = string.Format(
                "{0} Bet containing {1} constituents - BetId={2}, PlayerId={3}, Amount={4:f}, BonusAmount={5:f}",
                "Win", 3, "bet-123", "player-456", 100.0m, 25.50m);

            await EventFilter.Info(message: expected).ExpectOneAsync(() =>
            {
                PublishSemanticInfo(logMessage);
                return Task.CompletedTask;
            });
            TestSuccessful = true;
        }

        [Fact(DisplayName = "New named template - converted from positional - customer scenario")]
        public async Task Customer_scenario_new_named_template()
        {
            // After converting to named templates:
            // Logger.Info("{BetType} Bet containing {Count} constituents - BetId={BetId}, PlayerId={PlayerId}, Amount={Amount}, BonusAmount={BonusAmount}", ...)
            var logMessage = new DefaultLogMessage(
                SemanticLogMessageFormatter.Instance,
                "{BetType} Bet containing {Count} constituents - BetId={BetId}, PlayerId={PlayerId}, Amount={Amount}, BonusAmount={BonusAmount}",
                "Win", 3, "bet-123", "player-456", 100.0m, 25.50m);

            // Named templates use value.ToString() directly - decimal preserves trailing zeros
            await EventFilter.Info(
                message: "Win Bet containing 3 constituents - BetId=bet-123, PlayerId=player-456, Amount=100.0, BonusAmount=25.50")
                .ExpectOneAsync(() =>
                {
                    PublishSemanticInfo(logMessage);
                    return Task.CompletedTask;
                });
            TestSuccessful = true;
        }

        [Fact(DisplayName = "Named template with nullable decimal null renders as 'null' - customer scenario")]
        public async Task Customer_scenario_nullable_null_in_named_template()
        {
            // If bonusReturns is null
            var logMessage = new DefaultLogMessage(
                SemanticLogMessageFormatter.Instance,
                "{BetType} Bet - Amount={Amount}, BonusAmount={BonusAmount}",
                "Win", 50.0m, (object)null);

            // null renders as "null" in named templates
            await EventFilter.Info(
                message: "Win Bet - Amount=50.0, BonusAmount=null")
                .ExpectOneAsync(() =>
                {
                    PublishSemanticInfo(logMessage);
                    return Task.CompletedTask;
                });
            TestSuccessful = true;
        }

        [Fact(DisplayName = "Decimal precision mismatch: 100.0m vs 100m produce different formatted output")]
        public async Task Customer_scenario_decimal_precision_mismatch()
        {
            // Demonstrates the core issue: if the actor produces 100.0m but the test expects "100",
            // the EventFilter won't match because 100.0m.ToString() == "100.0"
            var logMessage100point0 = new DefaultLogMessage(
                SemanticLogMessageFormatter.Instance,
                "Returns: {Amount}", 100.0m);

            var logMessage100 = new DefaultLogMessage(
                SemanticLogMessageFormatter.Instance,
                "Returns: {Amount}", 100m);

            // 100.0m formats as "100.0" - filter for "100.0" matches it
            await EventFilter.Info(message: "Returns: 100.0").ExpectOneAsync(() =>
            {
                PublishSemanticInfo(logMessage100point0);
                return Task.CompletedTask;
            });

            // 100m formats as "100" - filter for "100" matches it
            await EventFilter.Info(message: "Returns: 100").ExpectOneAsync(() =>
            {
                PublishSemanticInfo(logMessage100);
                return Task.CompletedTask;
            });
            TestSuccessful = true;
        }

        [Fact(DisplayName = "Decimal precision mismatch: actor produces 100.0m but test uses 100m in interpolation")]
        public async Task Customer_scenario_interpolation_vs_semantic_decimal_mismatch()
        {
            // Reproduces exact customer issue: actor arithmetic produces 100.0m,
            // but test constructs the EventFilter using string interpolation with 100m.
            // $"Returns: {testValue}" → "Returns: 100" (interpolation uses 100m.ToString())
            // But the semantic log formats 100.0m → "Returns: 100.0"
            // These don't match, so the EventFilter never intercepts the message.
            var loggedValue = 100.0m; // what the actor produces (e.g. from 50.0m * 2)
            var testValue = 100m;     // what the test author writes

            var logMessage = new DefaultLogMessage(
                SemanticLogMessageFormatter.Instance,
                "Returns: {Amount}", loggedValue);

            // WRONG: using interpolation with a different decimal precision will not match
            // EventFilter.Info(message: $"Returns: {testValue}") won't work because
            // $"Returns: {100m}" → "Returns: 100" but the log produces "Returns: 100.0"

            // FIX 1: use `contains:` instead of `message:` for a partial match
            await EventFilter.Info(contains: "Returns:").ExpectOneAsync(() =>
            {
                PublishSemanticInfo(logMessage);
                return Task.CompletedTask;
            });

            // FIX 2: match the exact decimal precision the actor produces
            await EventFilter.Info(message: $"Returns: {loggedValue}").ExpectOneAsync(() =>
            {
                PublishSemanticInfo(logMessage);
                return Task.CompletedTask;
            });

            // FIX 3a: use F2 format specifier to normalize both sides to 2 decimal places
            var logMessageWithF2 = new DefaultLogMessage(
                SemanticLogMessageFormatter.Instance,
                "Returns: {Amount:F2}", loggedValue);
            // {Amount:F2} formats both 100.0m and 100m as "100.00"
            await EventFilter.Info(message: $"Returns: {testValue:F2}").ExpectOneAsync(() =>
            {
                PublishSemanticInfo(logMessageWithF2);
                return Task.CompletedTask;
            });

            // FIX 3b: use F0 to strip all decimal places
            var logMessageWithF0 = new DefaultLogMessage(
                SemanticLogMessageFormatter.Instance,
                "Returns: {Amount:F0}", loggedValue);
            // {Amount:F0} formats 100.0m as "100" — now matches the test's "100"
            await EventFilter.Info(message: $"Returns: {testValue:F0}").ExpectOneAsync(() =>
            {
                PublishSemanticInfo(logMessageWithF0);
                return Task.CompletedTask;
            });

            TestSuccessful = true;
        }

        #endregion

        #region WithContext Enrichment

        [Fact(DisplayName = "EventFilter still matches when context properties are attached")]
        public async Task WithContext_does_not_affect_EventFilter_matching()
        {
            // Context properties are stored separately and don't affect message matching
            var logMessage = new DefaultLogMessage(
                SemanticLogMessageFormatter.Instance,
                "User {UserId} logged in",
                42);

            // Create an Info event and attach context properties
            var infoEvent = new Info(GetType().FullName, GetType(), logMessage);
            infoEvent.SetContextProperties(
                new LogContextProperties(new[]
                {
                    new KeyValuePair<string, object>("RequestId", "abc-123"),
                    new KeyValuePair<string, object>("CorrelationId", "xyz-789")
                }));

            await EventFilter.Info(message: "User 42 logged in").ExpectOneAsync(() =>
            {
                Sys.EventStream.Publish(infoEvent);
                return Task.CompletedTask;
            });
            TestSuccessful = true;
        }

        #endregion

        #region Positional Template Backward Compatibility

        [Fact(DisplayName = "Positional template {0} still works with EventFilter via string.Format")]
        public async Task Positional_template_backward_compat()
        {
            var logMessage = new DefaultLogMessage(
                SemanticLogMessageFormatter.Instance,
                "User {0} logged in from {1}",
                42, "192.168.1.1");

            await EventFilter.Info(message: "User 42 logged in from 192.168.1.1").ExpectOneAsync(() =>
            {
                PublishSemanticInfo(logMessage);
                return Task.CompletedTask;
            });
            TestSuccessful = true;
        }

        [Fact(DisplayName = "Positional template with null: null renders as empty string")]
        public async Task Positional_template_null_renders_as_empty()
        {
            var logMessage = new DefaultLogMessage(
                SemanticLogMessageFormatter.Instance,
                "Value: [{0}]",
                new object[] { null });

            // string.Format("{0}", null) → "" so result is "Value: []"
            await EventFilter.Info(message: "Value: []").ExpectOneAsync(() =>
            {
                PublishSemanticInfo(logMessage);
                return Task.CompletedTask;
            });
            TestSuccessful = true;
        }

        #endregion

        #region Real Logger.Info() Path (End-to-End Scenario)

        [Fact(DisplayName = "Logger.Info with named template goes through SemanticLogMessageFormatter by default")]
        public async Task Real_logger_info_uses_semantic_formatter()
        {
            // Verify that the system's default formatter is SemanticLogMessageFormatter
            Sys.Settings.LogFormatter.Should().BeOfType<SemanticLogMessageFormatter>(
                "the default HOCON config sets akka.logger-formatter to SemanticLogMessageFormatter");

            // Use the real logging adapter — same code path as an actor's Logger property
            var logger = Logging.GetLogger(Sys, "TestBettingActor");

            // Named template with 6 args including decimals
            // This goes through Log<T1,...,T6> → new LogMessage<LogValues<T1,...,T6>>(log.Formatter, ...)
            await EventFilter.Info(
                message: "Win Bet containing 3 constituents - BetId=bet-123, PlayerId=player-456, Amount=100.0, BonusAmount=25.50")
                .ExpectOneAsync(() =>
                {
                    logger.Info(
                        "{BetType} Bet containing {Count} constituents - BetId={BetId}, PlayerId={PlayerId}, Amount={Amount}, BonusAmount={BonusAmount}",
                        "Win", 3, "bet-123", "player-456", 100.0m, 25.50m);
                    return Task.CompletedTask;
                });
            TestSuccessful = true;
        }

        [Fact(DisplayName = "Logger.Info with named template and null decimal arg")]
        public async Task Real_logger_info_with_null_decimal()
        {
            var logger = Logging.GetLogger(Sys, "TestBettingActor");

            // When bonusReturns is null
            await EventFilter.Info(
                message: "Win Bet - Amount=50.0, BonusAmount=null")
                .ExpectOneAsync(() =>
                {
                    logger.Info(
                        "{BetType} Bet - Amount={Amount}, BonusAmount={BonusAmount}",
                        "Win", 50.0m, (object)null);
                    return Task.CompletedTask;
                });
            TestSuccessful = true;
        }

        [Fact(DisplayName = "Default formatter is SemanticLogMessageFormatter")]
        public void Default_formatter_is_semantic()
        {
            // This test documents the critical requirement: the default formatter must be
            // SemanticLogMessageFormatter for named templates to work
            Sys.Settings.LogFormatter.Should().BeOfType<SemanticLogMessageFormatter>();

            // A logger obtained without explicit formatter should use the system default
            var logger = Logging.GetLogger(Sys, "test");
            logger.Formatter.Should().BeOfType<SemanticLogMessageFormatter>();
        }

        #endregion

        #region Generic LogMessage<LogValues<T>> Path

        [Fact(DisplayName = "LogMessage<LogValues<T>> works with EventFilter")]
        public async Task Generic_LogMessage_path_with_EventFilter()
        {
            var logValues = new LogValues<int>(42);
            var logMessage = new LogMessage<LogValues<int>>(
                SemanticLogMessageFormatter.Instance,
                "User {UserId} logged in",
                logValues);

            await EventFilter.Info(message: "User 42 logged in").ExpectOneAsync(() =>
            {
                PublishSemanticInfo(logMessage);
                return Task.CompletedTask;
            });
            TestSuccessful = true;
        }

        [Fact(DisplayName = "LogMessage<LogValues<T>> with nullable null works with EventFilter")]
        public async Task Generic_LogMessage_path_nullable_null_with_EventFilter()
        {
            decimal? value = null;
            var logValues = new LogValues<decimal?>(value);
            var logMessage = new LogMessage<LogValues<decimal?>>(
                SemanticLogMessageFormatter.Instance,
                "Amount: {Amount}",
                logValues);

            await EventFilter.Info(message: "Amount: null").ExpectOneAsync(() =>
            {
                PublishSemanticInfo(logMessage);
                return Task.CompletedTask;
            });
            TestSuccessful = true;
        }

        #endregion
    }
}
