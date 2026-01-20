// -----------------------------------------------------------------------
//  <copyright file="DiagnosticTimeline.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using Xunit.Abstractions;

namespace Akka.Persistence.Query.InMemory.Tests.Diagnostics;

/// <summary>
/// Thread-safe diagnostic timeline for capturing events during test execution.
/// Helps identify timing issues and race conditions.
/// </summary>
public static class DiagnosticTimeline
{
    private static readonly Stopwatch Stopwatch = new();
    private static readonly ConcurrentQueue<string> Events = new();
    private static int _isRunning;

    /// <summary>
    /// Start the timeline. Call this at the beginning of the test.
    /// </summary>
    public static void Start()
    {
        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) == 0)
        {
            while (Events.TryDequeue(out _)) { } // Clear any previous events
            Stopwatch.Restart();
            Record("Timeline started");
        }
    }

    /// <summary>
    /// Record an event with automatic timestamp and thread ID.
    /// </summary>
    /// <param name="message">The event message</param>
    /// <param name="caller">Automatically populated with caller method name</param>
    public static void Record(string message, [CallerMemberName] string caller = "")
    {
        if (_isRunning == 0) return;

        var elapsed = Stopwatch.ElapsedMilliseconds;
        var threadId = Thread.CurrentThread.ManagedThreadId;
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");

        Events.Enqueue($"[{elapsed,6}ms] [{timestamp}] [Thread {threadId:D3}] {caller}: {message}");
    }

    /// <summary>
    /// Record an event with a specific category tag.
    /// </summary>
    public static void Record(string category, string message, [CallerMemberName] string caller = "")
    {
        if (_isRunning == 0) return;

        var elapsed = Stopwatch.ElapsedMilliseconds;
        var threadId = Thread.CurrentThread.ManagedThreadId;
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");

        Events.Enqueue($"[{elapsed,6}ms] [{timestamp}] [Thread {threadId:D3}] [{category}] {caller}: {message}");
    }

    /// <summary>
    /// Stop the timeline and dump all events to the test output.
    /// </summary>
    public static void StopAndDump(ITestOutputHelper output)
    {
        if (Interlocked.CompareExchange(ref _isRunning, 0, 1) == 1)
        {
            Record("Timeline stopped");
            Stopwatch.Stop();

            output.WriteLine("");
            output.WriteLine("========== DIAGNOSTIC TIMELINE ==========");
            output.WriteLine($"Total duration: {Stopwatch.ElapsedMilliseconds}ms");
            output.WriteLine("");

            while (Events.TryDequeue(out var evt))
            {
                output.WriteLine(evt);
            }

            output.WriteLine("==========================================");
            output.WriteLine("");
        }
    }

    /// <summary>
    /// Check if the timeline is currently recording.
    /// </summary>
    public static bool IsRunning => _isRunning == 1;
}
