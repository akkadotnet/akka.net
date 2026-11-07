//-----------------------------------------------------------------------
// <copyright file="DiGreeterActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.Hosting.AOT.App.Actors;

/// <summary>
/// A plain DI service, registered in <c>IServiceCollection</c> and constructor-injected into
/// <see cref="DiGreeterActor"/> through Akka.DependencyInjection's <c>resolver.Props&lt;T&gt;()</c>.
/// </summary>
internal interface IGreetingService
{
    string Greet(string name);
}

internal sealed class GreetingService : IGreetingService
{
    public string Greet(string name) => $"hello, {name}";
}

/// <summary>
/// Created via <c>Akka.DependencyInjection</c>'s <see cref="DependencyResolver"/>
/// (<c>resolver.Props&lt;DiGreeterActor&gt;()</c>), so its constructor is populated from the host's
/// <c>IServiceProvider</c> rather than a plain <c>Props.Create</c>.
/// </summary>
internal sealed class DiGreeterActor : ReceiveActor
{
    public DiGreeterActor(IGreetingService greeter)
    {
        Receive<string>(name => Sender.Tell(greeter.Greet(name)));
    }
}
