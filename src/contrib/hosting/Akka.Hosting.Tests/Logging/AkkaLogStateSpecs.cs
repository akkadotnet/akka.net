// -----------------------------------------------------------------------
//  <copyright file="AkkaLogStateSpecs.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Akka.Hosting.Logging;
using FluentAssertions;
using Xunit;

namespace Akka.Hosting.Tests.Logging;

public class AkkaLogStateSpecs
{
    [Fact]
    public void AkkaLogState_WithSemanticProperties_ShouldYieldAllProperties()
    {
        // Arrange
        var traceId = ActivityTraceId.CreateRandom();
        var spanId = ActivitySpanId.CreateRandom();
        var activityContext = new ActivityContext(traceId, spanId, ActivityTraceFlags.Recorded);

        var semanticProperties = new Dictionary<string, object>
        {
            { "UserId", 123 },
            { "Action", "Login" }
        };

        var actorPath = "akka://test/user/myactor";
        var timestamp = DateTimeOffset.UtcNow;
        var threadId = 42;
        var logSource = "MyActor";
        var template = "User {UserId} performed {Action}";
        var formattedMessage = "User 123 performed Login";

        // Act
        var state = new AkkaLogState(
            activityContext,
            semanticProperties,
            actorPath,
            timestamp,
            threadId,
            logSource,
            template,
            formattedMessage);

        var items = state.ToList();

        // Assert
        // Should have: 3 trace context + 2 semantic props + 4 Akka metadata + 1 OriginalFormat = 10
        items.Should().HaveCount(10);

        // Verify trace context (should be ActivityTraceId/ActivitySpanId, not strings)
        items.Should().Contain(kvp => kvp.Key == AkkaLogState.TraceIdKey && kvp.Value is ActivityTraceId);
        items.Should().Contain(kvp => kvp.Key == AkkaLogState.SpanIdKey && kvp.Value is ActivitySpanId);
        items.Should().Contain(kvp => kvp.Key == AkkaLogState.TraceFlagsKey && (int)kvp.Value! == (int)ActivityTraceFlags.Recorded);

        // Verify semantic properties
        items.Should().Contain(kvp => kvp.Key == "UserId" && (int)kvp.Value! == 123);
        items.Should().Contain(kvp => kvp.Key == "Action" && (string)kvp.Value! == "Login");

        // Verify Akka metadata
        items.Should().Contain(kvp => kvp.Key == "ActorPath" && (string)kvp.Value! == actorPath);
        items.Should().Contain(kvp => kvp.Key == "Timestamp" && (DateTimeOffset)kvp.Value! == timestamp);
        items.Should().Contain(kvp => kvp.Key == "Thread" && (int)kvp.Value! == threadId);
        items.Should().Contain(kvp => kvp.Key == "LogSource" && (string)kvp.Value! == logSource);

        // Verify OriginalFormat
        items.Should().Contain(kvp => kvp.Key == "{OriginalFormat}" && (string)kvp.Value! == template);
    }

    [Fact]
    public void AkkaLogState_WithoutTraceContext_ShouldOmitTraceProperties()
    {
        // Arrange
        var activityContext = default(ActivityContext); // No trace context

        var semanticProperties = new Dictionary<string, object>
        {
            { "Message", "Hello" }
        };

        // Act
        var state = new AkkaLogState(
            activityContext,
            semanticProperties,
            "akka://test/user/actor",
            DateTimeOffset.UtcNow,
            1,
            "Source",
            "Template",
            "Formatted");

        var items = state.ToList();

        // Assert
        // Should have: 0 trace context + 1 semantic prop + 4 Akka metadata + 1 OriginalFormat = 6
        items.Should().HaveCount(6);

        // Should NOT contain trace context keys
        items.Should().NotContain(kvp => kvp.Key == AkkaLogState.TraceIdKey);
        items.Should().NotContain(kvp => kvp.Key == AkkaLogState.SpanIdKey);
        items.Should().NotContain(kvp => kvp.Key == AkkaLogState.TraceFlagsKey);
    }

    [Fact]
    public void AkkaLogState_NonStructuredMessage_ShouldYieldMetadataProperties()
    {
        // Arrange
        var traceId = ActivityTraceId.CreateRandom();
        var spanId = ActivitySpanId.CreateRandom();
        var activityContext = new ActivityContext(traceId, spanId, ActivityTraceFlags.None);
        var message = "Plain text message";
        var actorPath = "akka://test/user/myactor";
        var timestamp = DateTimeOffset.UtcNow;
        var threadId = 42;
        var logSource = "MyActor";

        // Act
        var state = new AkkaLogState(activityContext, actorPath, timestamp, threadId, logSource, message, message);
        var items = state.ToList();

        // Assert
        // Should have: 3 trace context + 4 Akka metadata + 1 OriginalFormat = 8
        items.Should().HaveCount(8);

        // Verify trace context is present
        items.Should().Contain(kvp => kvp.Key == AkkaLogState.TraceIdKey);
        items.Should().Contain(kvp => kvp.Key == AkkaLogState.SpanIdKey);
        items.Should().Contain(kvp => kvp.Key == AkkaLogState.TraceFlagsKey);

        // Verify Akka metadata
        items.Should().Contain(kvp => kvp.Key == "ActorPath" && (string)kvp.Value! == actorPath);
        items.Should().Contain(kvp => kvp.Key == "Timestamp" && (DateTimeOffset)kvp.Value! == timestamp);
        items.Should().Contain(kvp => kvp.Key == "Thread" && (int)kvp.Value! == threadId);
        items.Should().Contain(kvp => kvp.Key == "LogSource" && (string)kvp.Value! == logSource);

        // Verify OriginalFormat
        items.Should().Contain(kvp => kvp.Key == "{OriginalFormat}" && (string)kvp.Value! == message);
    }

    [Fact]
    public void AkkaLogState_NonStructuredMessage_WithoutTraceContext_ShouldYieldMetadataAndOriginalFormat()
    {
        // Arrange
        var activityContext = default(ActivityContext);
        var message = "Plain text message";
        var actorPath = "akka://test/user/myactor";
        var timestamp = DateTimeOffset.UtcNow;
        var threadId = 7;
        var logSource = "MyActor";

        // Act
        var state = new AkkaLogState(activityContext, actorPath, timestamp, threadId, logSource, message, message);
        var items = state.ToList();

        // Assert
        // Should have: 0 trace context + 4 Akka metadata + 1 OriginalFormat = 5
        items.Should().HaveCount(5);
        items.Should().Contain(kvp => kvp.Key == "ActorPath" && (string)kvp.Value! == actorPath);
        items.Should().Contain(kvp => kvp.Key == "Timestamp" && (DateTimeOffset)kvp.Value! == timestamp);
        items.Should().Contain(kvp => kvp.Key == "Thread" && (int)kvp.Value! == threadId);
        items.Should().Contain(kvp => kvp.Key == "LogSource" && (string)kvp.Value! == logSource);
        items.Should().Contain(kvp => kvp.Key == "{OriginalFormat}" && (string)kvp.Value! == message);
    }

    [Fact]
    public void AkkaLogState_ToString_ShouldReturnFormattedMessage()
    {
        // Arrange
        var semanticProperties = new Dictionary<string, object> { { "Name", "World" } };
        var formattedMessage = "Hello World!";

        var state = new AkkaLogState(
            default,
            semanticProperties,
            "path",
            DateTimeOffset.UtcNow,
            1,
            "source",
            "Hello {Name}!",
            formattedMessage);

        // Act & Assert
        state.ToString().Should().Be(formattedMessage);
    }

    [Fact]
    public void AkkaLogState_ShouldBeEnumerableMultipleTimes()
    {
        // Arrange
        var semanticProperties = new Dictionary<string, object> { { "Key", "Value" } };
        var state = new AkkaLogState(
            default,
            semanticProperties,
            "path",
            DateTimeOffset.UtcNow,
            1,
            "source",
            "template",
            "formatted");

        // Act - enumerate multiple times
        var firstPass = state.ToList();
        var secondPass = state.ToList();

        // Assert
        firstPass.Should().BeEquivalentTo(secondPass);
    }

    [Fact]
    public void AkkaLogState_TraceContext_ShouldStoreStructsDirectly()
    {
        // Arrange - this test verifies we're not allocating strings for TraceId/SpanId
        var traceId = ActivityTraceId.CreateRandom();
        var spanId = ActivitySpanId.CreateRandom();
        var activityContext = new ActivityContext(traceId, spanId, ActivityTraceFlags.Recorded);

        var state = new AkkaLogState(
            activityContext,
            new Dictionary<string, object>(),
            "path",
            DateTimeOffset.UtcNow,
            1,
            "source",
            "template",
            "formatted");

        // Act
        var items = state.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        // Assert - values should be the actual struct types, not strings
        items[AkkaLogState.TraceIdKey].Should().BeOfType<ActivityTraceId>();
        items[AkkaLogState.SpanIdKey].Should().BeOfType<ActivitySpanId>();
        items[AkkaLogState.TraceFlagsKey].Should().BeOfType<int>();

        // Verify the actual values match
        ((ActivityTraceId)items[AkkaLogState.TraceIdKey]!).Should().Be(traceId);
        ((ActivitySpanId)items[AkkaLogState.SpanIdKey]!).Should().Be(spanId);
        ((int)items[AkkaLogState.TraceFlagsKey]!).Should().Be((int)ActivityTraceFlags.Recorded);
    }

    [Fact]
    public void AkkaLogState_EmptySemanticProperties_ShouldStillYieldAkkaMetadata()
    {
        // Arrange
        var emptyProperties = new Dictionary<string, object>();

        var state = new AkkaLogState(
            default,
            emptyProperties,
            "akka://test/user/actor",
            DateTimeOffset.UtcNow,
            99,
            "TestSource",
            "template",
            "formatted");

        // Act
        var items = state.ToList();

        // Assert
        // Should have: 0 trace context + 0 semantic props + 4 Akka metadata + 1 OriginalFormat = 5
        items.Should().HaveCount(5);
        items.Should().Contain(kvp => kvp.Key == "ActorPath");
        items.Should().Contain(kvp => kvp.Key == "Timestamp");
        items.Should().Contain(kvp => kvp.Key == "Thread" && (int)kvp.Value! == 99);
        items.Should().Contain(kvp => kvp.Key == "LogSource" && (string)kvp.Value! == "TestSource");
        items.Should().Contain(kvp => kvp.Key == "{OriginalFormat}");
    }
}
