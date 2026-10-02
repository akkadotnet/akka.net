// -----------------------------------------------------------------------
//  <copyright file="AkkaTraceContextProcessorSpecs.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Akka.Hosting.Logging;
using FluentAssertions;
using OpenTelemetry.Logs;
using Xunit;

namespace Akka.Hosting.Tests.Logging;

public class AkkaTraceContextProcessorSpecs
{
    [Fact]
    public void Processor_ShouldExtractTraceContext_FromActivityTraceIdAndSpanId()
    {
        // Arrange
        var processor = new AkkaTraceContextProcessor();
        var traceId = ActivityTraceId.CreateRandom();
        var spanId = ActivitySpanId.CreateRandom();
        var traceFlags = ActivityTraceFlags.Recorded;

        var attributes = new List<KeyValuePair<string, object?>>
        {
            new(AkkaLogState.TraceIdKey, traceId),
            new(AkkaLogState.SpanIdKey, spanId),
            new(AkkaLogState.TraceFlagsKey, (int)traceFlags)
        };

        var logRecord = CreateLogRecord(attributes);

        // Act
        processor.OnEnd(logRecord);

        // Assert
        logRecord.TraceId.Should().Be(traceId);
        logRecord.SpanId.Should().Be(spanId);
        logRecord.TraceFlags.Should().Be(traceFlags);
    }

    [Fact]
    public void Processor_ShouldExtractTraceContext_FromStringRepresentation()
    {
        // Arrange
        var processor = new AkkaTraceContextProcessor();
        var traceId = ActivityTraceId.CreateRandom();
        var spanId = ActivitySpanId.CreateRandom();

        // Use string representation (backwards compatibility path)
        var attributes = new List<KeyValuePair<string, object?>>
        {
            new(AkkaLogState.TraceIdKey, traceId.ToString()),
            new(AkkaLogState.SpanIdKey, spanId.ToString()),
            new(AkkaLogState.TraceFlagsKey, (int)ActivityTraceFlags.None)
        };

        var logRecord = CreateLogRecord(attributes);

        // Act
        processor.OnEnd(logRecord);

        // Assert
        logRecord.TraceId.Should().Be(traceId);
        logRecord.SpanId.Should().Be(spanId);
    }

    [Fact]
    public void Processor_ShouldSkip_WhenTraceIdAlreadySet()
    {
        // Arrange
        var processor = new AkkaTraceContextProcessor();
        var existingTraceId = ActivityTraceId.CreateRandom();
        var existingSpanId = ActivitySpanId.CreateRandom();
        var newTraceId = ActivityTraceId.CreateRandom();
        var newSpanId = ActivitySpanId.CreateRandom();

        var attributes = new List<KeyValuePair<string, object?>>
        {
            new(AkkaLogState.TraceIdKey, newTraceId),
            new(AkkaLogState.SpanIdKey, newSpanId),
            new(AkkaLogState.TraceFlagsKey, (int)ActivityTraceFlags.Recorded)
        };

        var logRecord = CreateLogRecordWithExistingTrace(attributes, existingTraceId, existingSpanId);

        // Act
        processor.OnEnd(logRecord);

        // Assert - should keep existing trace context
        logRecord.TraceId.Should().Be(existingTraceId);
        logRecord.SpanId.Should().Be(existingSpanId);
    }

    [Fact]
    public void Processor_ShouldSkip_WhenNoAttributes()
    {
        // Arrange
        var processor = new AkkaTraceContextProcessor();
        var logRecord = CreateLogRecord(null);

        // Act
        processor.OnEnd(logRecord);

        // Assert - should remain default
        logRecord.TraceId.Should().Be(default(ActivityTraceId));
        logRecord.SpanId.Should().Be(default(ActivitySpanId));
    }

    [Fact]
    public void Processor_ShouldSkip_WhenMissingTraceId()
    {
        // Arrange
        var processor = new AkkaTraceContextProcessor();
        var spanId = ActivitySpanId.CreateRandom();

        var attributes = new List<KeyValuePair<string, object?>>
        {
            // Missing TraceId
            new(AkkaLogState.SpanIdKey, spanId),
            new(AkkaLogState.TraceFlagsKey, 0)
        };

        var logRecord = CreateLogRecord(attributes);

        // Act
        processor.OnEnd(logRecord);

        // Assert - should not set partial trace context
        logRecord.TraceId.Should().Be(default(ActivityTraceId));
        logRecord.SpanId.Should().Be(default(ActivitySpanId));
    }

    [Fact]
    public void Processor_ShouldSkip_WhenMissingSpanId()
    {
        // Arrange
        var processor = new AkkaTraceContextProcessor();
        var traceId = ActivityTraceId.CreateRandom();

        var attributes = new List<KeyValuePair<string, object?>>
        {
            new(AkkaLogState.TraceIdKey, traceId),
            // Missing SpanId
            new(AkkaLogState.TraceFlagsKey, 0)
        };

        var logRecord = CreateLogRecord(attributes);

        // Act
        processor.OnEnd(logRecord);

        // Assert - should not set partial trace context
        logRecord.TraceId.Should().Be(default(ActivityTraceId));
        logRecord.SpanId.Should().Be(default(ActivitySpanId));
    }

    [Fact]
    public void Processor_ShouldHandle_InvalidStringTraceId()
    {
        // Arrange
        var processor = new AkkaTraceContextProcessor();
        var spanId = ActivitySpanId.CreateRandom();

        var attributes = new List<KeyValuePair<string, object?>>
        {
            new(AkkaLogState.TraceIdKey, "invalid-trace-id"), // Invalid format
            new(AkkaLogState.SpanIdKey, spanId),
            new(AkkaLogState.TraceFlagsKey, 0)
        };

        var logRecord = CreateLogRecord(attributes);

        // Act
        processor.OnEnd(logRecord);

        // Assert - should not crash, should not set invalid trace context
        logRecord.TraceId.Should().Be(default(ActivityTraceId));
    }

    [Fact]
    public void Processor_ShouldHandle_UnexpectedValueTypes()
    {
        // Arrange
        var processor = new AkkaTraceContextProcessor();

        var attributes = new List<KeyValuePair<string, object?>>
        {
            new(AkkaLogState.TraceIdKey, 12345), // Wrong type (int instead of ActivityTraceId or string)
            new(AkkaLogState.SpanIdKey, new object()), // Wrong type
            new(AkkaLogState.TraceFlagsKey, "not-an-int")
        };

        var logRecord = CreateLogRecord(attributes);

        // Act - should not throw
        var act = () => processor.OnEnd(logRecord);

        // Assert
        act.Should().NotThrow();
        logRecord.TraceId.Should().Be(default(ActivityTraceId));
    }

    [Fact]
    public void Processor_ShouldDefaultTraceFlags_WhenNotProvided()
    {
        // Arrange
        var processor = new AkkaTraceContextProcessor();
        var traceId = ActivityTraceId.CreateRandom();
        var spanId = ActivitySpanId.CreateRandom();

        var attributes = new List<KeyValuePair<string, object?>>
        {
            new(AkkaLogState.TraceIdKey, traceId),
            new(AkkaLogState.SpanIdKey, spanId)
            // No TraceFlags
        };

        var logRecord = CreateLogRecord(attributes);

        // Act
        processor.OnEnd(logRecord);

        // Assert
        logRecord.TraceId.Should().Be(traceId);
        logRecord.SpanId.Should().Be(spanId);
        logRecord.TraceFlags.Should().Be(ActivityTraceFlags.None);
    }

    [Fact]
    public void Processor_ShouldWork_WithAkkaLogState()
    {
        // Arrange - integration test with actual AkkaLogState
        var processor = new AkkaTraceContextProcessor();
        var traceId = ActivityTraceId.CreateRandom();
        var spanId = ActivitySpanId.CreateRandom();
        var activityContext = new ActivityContext(traceId, spanId, ActivityTraceFlags.Recorded);

        var state = new AkkaLogState(
            activityContext,
            new Dictionary<string, object> { { "Key", "Value" } },
            "akka://test/user/actor",
            DateTimeOffset.UtcNow,
            1,
            "TestSource",
            "Template with {Key}",
            "Template with Value");

        // Convert state to attributes list (simulating what MEL does)
        var attributes = new List<KeyValuePair<string, object?>>();
        foreach (var kvp in state)
        {
            attributes.Add(kvp);
        }

        var logRecord = CreateLogRecord(attributes);

        // Act
        processor.OnEnd(logRecord);

        // Assert
        logRecord.TraceId.Should().Be(traceId);
        logRecord.SpanId.Should().Be(spanId);
        logRecord.TraceFlags.Should().Be(ActivityTraceFlags.Recorded);
    }

    /// <summary>
    /// Creates a LogRecord instance using UnsafeAccessor to call the internal constructor.
    /// </summary>
    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    private static extern LogRecord CreateLogRecordInstance();

    /// <summary>
    /// Sets the Attributes property on a LogRecord using UnsafeAccessor.
    /// </summary>
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_Attributes")]
    private static extern void SetAttributes(LogRecord record, IReadOnlyList<KeyValuePair<string, object?>>? value);

    /// <summary>
    /// Sets the TraceId property on a LogRecord using UnsafeAccessor.
    /// </summary>
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_TraceId")]
    private static extern void SetTraceId(LogRecord record, ActivityTraceId value);

    /// <summary>
    /// Sets the SpanId property on a LogRecord using UnsafeAccessor.
    /// </summary>
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_SpanId")]
    private static extern void SetSpanId(LogRecord record, ActivitySpanId value);

    /// <summary>
    /// Creates a LogRecord with the specified attributes.
    /// </summary>
    private static LogRecord CreateLogRecord(IReadOnlyList<KeyValuePair<string, object?>>? attributes)
    {
        var logRecord = CreateLogRecordInstance();

        if (attributes != null)
        {
            SetAttributes(logRecord, attributes);
        }

        return logRecord;
    }

    /// <summary>
    /// Creates a LogRecord with existing trace context set.
    /// </summary>
    private static LogRecord CreateLogRecordWithExistingTrace(
        IReadOnlyList<KeyValuePair<string, object?>>? attributes,
        ActivityTraceId traceId,
        ActivitySpanId spanId)
    {
        var logRecord = CreateLogRecord(attributes);

        SetTraceId(logRecord, traceId);
        SetSpanId(logRecord, spanId);

        return logRecord;
    }
}
