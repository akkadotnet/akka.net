//-----------------------------------------------------------------------
// <copyright file="Actors.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.Maui.AOT.App.Actors;

/// <summary>Starts the ticker. <paramref name="Count"/> 0 means tick until the app exits.</summary>
public sealed record StartTicking(int Count, TimeSpan Interval);

/// <summary>One reading, sent from <see cref="TickerActor"/> to <see cref="MetricsActor"/>.</summary>
public sealed record Sample(int Sequence, double Value);

/// <summary>What <see cref="MetricsActor"/> pushes to the screen after every sample.</summary>
public sealed record MetricsSnapshot(int Samples, double Average, double Last);

public sealed record GetSampleCount
{
    public static readonly GetSampleCount Instance = new();
}

public sealed record SampleCount(int Count);

/// <summary>
/// Turns a periodic Akka timer into <see cref="Sample"/> messages for <see cref="MetricsActor"/>.
/// </summary>
public sealed class TickerActor : ReceiveActor, IWithTimers
{
    private sealed class Tick
    {
        public static readonly Tick Instance = new();
    }

    private const string TimerKey = "tick";

    private int _sequence;
    private int _limit;

    public ITimerScheduler Timers { get; set; } = null!;

    public TickerActor(IActorRef metrics)
    {
        Receive<StartTicking>(start =>
        {
            _limit = start.Count;
            Timers.StartPeriodicTimer(TimerKey, Tick.Instance, start.Interval);
        });

        Receive<Tick>(_ =>
        {
            _sequence++;

            // a made-up gauge that moves around between 0 and 100
            var value = (_sequence * 37 % 101) * 0.99;
            metrics.Tell(new Sample(_sequence, value));

            if (_limit > 0 && _sequence >= _limit)
                Timers.Cancel(TimerKey);
        });
    }
}

/// <summary>
/// Aggregates samples and pushes a <see cref="MetricsSnapshot"/> to the UI for each one.
/// </summary>
public sealed class MetricsActor : ReceiveActor
{
    private int _count;
    private double _total;

    public MetricsActor(MetricsViewModel viewModel)
    {
        Receive<Sample>(sample =>
        {
            _count++;
            _total += sample.Value;

            // runs on an Akka dispatcher thread; Post marshals onto the UI thread
            viewModel.Post(new MetricsSnapshot(_count, _total / _count, sample.Value));
        });

        Receive<GetSampleCount>(_ => Sender.Tell(new SampleCount(_count)));
    }
}
