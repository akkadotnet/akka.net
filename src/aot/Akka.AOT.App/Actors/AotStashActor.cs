//-----------------------------------------------------------------------
// <copyright file="AotStashActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.AOT.App.Actors;

/// <summary>
/// Starts locked: anything but "open" is stashed. "open" becomes <see cref="Open"/>, replays the
/// stash, and replies "opened".
/// </summary>
public sealed class AotStashActor : ReceiveActor, IWithUnboundedStash
{
    public IStash Stash { get; set; } = null!;

    public AotStashActor()
    {
        Locked();
    }

    private void Locked()
    {
        Receive<string>(msg =>
        {
            if (msg == "open")
            {
                var replyTo = Sender;
                Become(Open);
                Stash.UnstashAll();
                replyTo.Tell("opened");
            }
            else
            {
                Stash.Stash();
            }
        });
    }

    private void Open()
    {
        Receive<string>(msg => Sender.Tell($"open:{msg}"));
    }
}
