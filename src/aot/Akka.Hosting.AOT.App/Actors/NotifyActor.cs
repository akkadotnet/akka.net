//-----------------------------------------------------------------------
// <copyright file="NotifyActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.Hosting.AOT.App.Actors;

/// <summary>
/// A plain DI service that a fire-and-forget <c>Tell</c> lands on, with no reply expected - the
/// pure "tell" counterpart to <see cref="EchoActor"/> and <see cref="DiGreeterActor"/>, which both
/// round-trip through <c>Ask</c>.
/// </summary>
internal interface INotificationSink
{
    Task<string> Received { get; }
    void Notify(string message);
}

internal sealed class NotificationSink : INotificationSink
{
    private readonly TaskCompletionSource<string> _tcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<string> Received => _tcs.Task;

    public void Notify(string message) => _tcs.TrySetResult(message);
}

/// <summary>
/// Created via <c>Akka.DependencyInjection</c>'s <see cref="DependencyResolver"/>
/// (<c>resolver.Props&lt;NotifyActor&gt;()</c>), exactly like <see cref="DiGreeterActor"/>. Receives
/// one-way notifications: the caller only <c>Tell</c>s, never <c>Ask</c>s, and the actor never
/// replies.
/// </summary>
internal sealed class NotifyActor : ReceiveActor
{
    public NotifyActor(INotificationSink sink)
    {
        Receive<string>(msg => sink.Notify(msg));
    }
}
