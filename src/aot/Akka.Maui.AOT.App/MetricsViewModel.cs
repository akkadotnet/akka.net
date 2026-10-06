//-----------------------------------------------------------------------
// <copyright file="MetricsViewModel.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Akka.Maui.AOT.App.Actors;

namespace Akka.Maui.AOT.App;

/// <summary>
/// What the page binds to. <see cref="MetricsActor"/> calls <see cref="Post"/> from a dispatcher thread; every
/// property changes on the UI thread only.
/// </summary>
public sealed class MetricsViewModel : INotifyPropertyChanged
{
    private int _offUiThreadChanges;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string SamplesText { get; private set; } = FormatSamples(0);

    public string AverageText { get; private set; } = "Average: -";

    /// <summary>The last sample as a fraction, for the progress bar.</summary>
    public double Level { get; private set; }

    /// <summary>Property changes raised off the UI thread. Must stay 0.</summary>
    public int OffUiThreadChanges => Volatile.Read(ref _offUiThreadChanges);

    public static string FormatSamples(int samples) => $"Samples: {samples}";

    /// <summary>Called by <see cref="MetricsActor"/>. Never touches a property itself.</summary>
    public void Post(MetricsSnapshot snapshot) => MainThread.BeginInvokeOnMainThread(() => Apply(snapshot));

    private void Apply(MetricsSnapshot snapshot)
    {
        SamplesText = FormatSamples(snapshot.Samples);
        AverageText = string.Create(CultureInfo.InvariantCulture, $"Average: {snapshot.Average:F1}  Last: {snapshot.Last:F1}");
        Level = snapshot.Last / 100;

        OnPropertyChanged(nameof(SamplesText));
        OnPropertyChanged(nameof(AverageText));
        OnPropertyChanged(nameof(Level));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        if (!MainThread.IsMainThread)
            Interlocked.Increment(ref _offUiThreadChanges);

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
