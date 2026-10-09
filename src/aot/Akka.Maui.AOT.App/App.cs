//-----------------------------------------------------------------------
// <copyright file="App.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Maui.AOT.App;

public sealed class App : Application
{
    private readonly MainPage _page;

    public App(MainPage page)
    {
        _page = page;
    }

    protected override Window CreateWindow(IActivationState? activationState) => new(_page) { Title = Strings.Title };
}
