//-----------------------------------------------------------------------
// <copyright file="MainPage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Hosting;
using Akka.Maui.AOT.App.Actors;

namespace Akka.Maui.AOT.App;

/// <summary>
/// The screen: three controls bound to <see cref="MetricsViewModel"/> with compiled (lambda) bindings, which is
/// what Native AOT requires. Built in C# rather than XAML to keep the canary small; the bindings are the same.
/// </summary>
public sealed class MainPage : ContentPage
{
    private readonly IRequiredActor<TickerActor> _ticker;
    private readonly List<(string? Text, bool OnUiThread)> _samplesLabelChanges = [];

    public MainPage(MetricsViewModel viewModel, IRequiredActor<TickerActor> ticker, IServiceProvider services)
    {
        _ticker = ticker;
        Services = services;
        ViewModel = viewModel;
        Title = Strings.Title;

        SamplesLabel = new Label { FontSize = 32, AutomationId = "SamplesLabel" };
        SamplesLabel.SetBinding(Label.TextProperty, static (MetricsViewModel vm) => vm.SamplesText);
        SamplesLabel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == Label.TextProperty.PropertyName)
                _samplesLabelChanges.Add((SamplesLabel.Text, MainThread.IsMainThread));
        };

        var average = new Label { FontSize = 18 };
        average.SetBinding(Label.TextProperty, static (MetricsViewModel vm) => vm.AverageText);

        var level = new ProgressBar();
        level.SetBinding(ProgressBar.ProgressProperty, static (MetricsViewModel vm) => vm.Level);

        BindingContext = viewModel;
        Content = new VerticalStackLayout
        {
            Padding = 24,
            Spacing = 16,
            Children =
            {
                new Label { Text = Strings.Title, FontSize = 24 },
                new Label { Text = Strings.Subtitle },
                SamplesLabel,
                average,
                level
            }
        };

        Loaded += OnLoaded;
    }

    public IServiceProvider Services { get; }

    public MetricsViewModel ViewModel { get; }

    public Label SamplesLabel { get; }

    /// <summary>Every value the samples label's Text took, in order. Read and written on the UI thread only.</summary>
    public IReadOnlyList<(string? Text, bool OnUiThread)> SamplesLabelChanges => _samplesLabelChanges;

    private void OnLoaded(object? sender, EventArgs e)
    {
        Loaded -= OnLoaded;

        if (CanaryRun.SelfTest)
        {
            // the self-test starts the ticker itself, with a fixed count, once it has a baseline
            _ = SelfTest.RunAsync(this, _ticker.ActorRef);
            return;
        }

        _ticker.ActorRef.Tell(new StartTicking(0, TimeSpan.FromMilliseconds(500)));
    }
}
