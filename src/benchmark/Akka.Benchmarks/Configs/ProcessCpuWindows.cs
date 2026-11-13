//-----------------------------------------------------------------------
// <copyright file="ProcessCpuWindows.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Akka.Benchmarks.Configs;

internal static class ProcessCpuWindows
{
    private static EventListener? _listener;

    [ModuleInitializer]
    internal static void Initialize()
    {
        if (Environment.GetEnvironmentVariable("AKKA_BENCHMARK_CPU_WINDOWS") != "1")
            return;

        WindowState.WarmUp();
        _listener = new CpuWindowListener();
        GC.KeepAlive(_listener);
    }

    private sealed class CpuWindowListener : EventListener
    {
        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name == "BenchmarkDotNet.EngineEventSource")
                EnableEvents(eventSource, EventLevel.Informational);
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (eventData.EventId is not (1 or 2 or 15 or 16))
                return;

            try
            {
                WindowState.Record(eventData);
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("AKKA_BDN_CPU_WINDOW_ERROR " + error);
                throw;
            }
        }
    }

    private static class WindowState
    {
        private static readonly Process CurrentProcess = Process.GetCurrentProcess();
        private static readonly object Gate = new();
        private static string? _benchmark;
        private static long _startTicks;
        private static long _operations;
        private static int _sequence;
        private static bool _active;

        internal static void WarmUp() => _ = ReadTicks();

        private static long ReadTicks()
        {
            CurrentProcess.Refresh();
            return CurrentProcess.TotalProcessorTime.Ticks;
        }

        internal static void Record(EventWrittenEventArgs eventData)
        {
            lock (Gate)
            {
                if (eventData.EventId is 1 or 2)
                {
                    if (eventData.Payload is not { Count: 1 } || eventData.Payload[0] is not string name)
                        throw new InvalidOperationException("Invalid BenchmarkDotNet benchmark name payload.");

                    if (eventData.EventId == 1)
                    {
                        if (_benchmark is not null || _active)
                            throw new InvalidOperationException("Benchmark start overlapped an existing benchmark.");
                        _benchmark = name;
                        _sequence = 0;
                    }
                    else
                    {
                        if (_active || _benchmark != name)
                            throw new InvalidOperationException("Benchmark stop did not match its start.");
                        Console.WriteLine("AKKA_BDN_CPU_COMPLETE " + JsonSerializer.Serialize(new
                        {
                            pid = CurrentProcess.Id,
                            benchmark = name,
                            windows = _sequence
                        }));
                        _benchmark = null;
                    }
                    return;
                }

                var ticks = ReadTicks();
                if (eventData.Payload is not { Count: 1 } || eventData.Payload[0] is not long operations || operations <= 0)
                    throw new InvalidOperationException("Invalid BenchmarkDotNet operation count payload.");
                if (_benchmark is null)
                    throw new InvalidOperationException("Workload event has no active benchmark.");

                if (eventData.EventId == 15)
                {
                    if (_active)
                        throw new InvalidOperationException("Workload start overlapped an existing CPU window.");
                    _active = true;
                    _startTicks = ticks;
                    _operations = operations;
                }
                else
                {
                    if (!_active || _operations != operations || ticks < _startTicks)
                        throw new InvalidOperationException("Workload stop did not match its CPU window.");
                    _active = false;
                    _sequence++;
                    Console.WriteLine("AKKA_BDN_CPU_WINDOW " + JsonSerializer.Serialize(new
                    {
                        pid = CurrentProcess.Id,
                        benchmark = _benchmark,
                        sequence = _sequence,
                        operations,
                        startTicks = _startTicks,
                        stopTicks = ticks
                    }));
                }
            }
        }
    }
}
