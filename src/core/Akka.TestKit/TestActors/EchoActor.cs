//-----------------------------------------------------------------------
// <copyright file="EchoActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

#nullable enable
namespace Akka.TestKit.TestActors;

/// <summary>
/// An <see cref="EchoActor"/> is an actor that echoes whatever is sent to it, to the
/// TestKit's <see cref="TestKitBase.TestActor"/>.
/// By default it also echoes back to the sender, unless the sender is the <see cref="TestKitBase.TestActor"/>
/// (in this case the <see cref="TestKitBase.TestActor"/> will only receive one message).
/// </summary>
public class EchoActor : ReceiveActor
{
    /// <summary>
    /// Creates an actor that sends received messages to the test actor and, optionally, to their original sender.
    /// </summary>
    /// <param name="testkit">The test kit whose test actor receives each message.</param>
    /// <param name="echoBackToSenderAsWell">Whether to also forward each message to its sender when that sender is not the test actor.</param>
    public EchoActor(TestKitBase testkit, bool echoBackToSenderAsWell = true)
    {
        ReceiveAny(msg =>
        {
            var sender = Sender;
            var testActor = testkit.TestActor;
            if (echoBackToSenderAsWell && testActor != sender)
                sender.Forward(msg);
            testActor.Tell(msg, Sender);
        });
    }

    /// <summary>
    /// Returns a <see cref="Props"/> object that can be used to create an <see cref="EchoActor"/>.
    /// The <see cref="EchoActor"/> echoes whatever is sent to it, to the
    /// TestKit's <see cref="TestKitBase.TestActor"/>.
    /// By default it also echoes back to the sender, unless the sender is the <see cref="TestKitBase.TestActor"/>
    /// (in this case the <see cref="TestKitBase.TestActor"/> will only receive one message) or unless
    /// <paramref name="echoBackToSenderAsWell"/> has been set to <c>false</c>.
    /// </summary>
    /// <param name="testkit">The test kit whose test actor receives each message.</param>
    /// <param name="echoBackToSenderAsWell">Whether to also forward each message to its sender when that sender is not the test actor.</param>
    /// <returns>Props for creating the echo actor.</returns>
    public static Props Props(TestKitBase testkit, bool echoBackToSenderAsWell = true)
    {
        return Actor.Props.Create(() => new EchoActor(testkit, echoBackToSenderAsWell));
    }
}

/// <summary>
/// An <see cref="SimpleEchoActor"/> is an actor that echoes whatever is sent to it, to the `Sender`.
/// </summary>
public class SimpleEchoActor : ReceiveActor
{
    /// <summary>
    /// Creates a simple actor that replies to each message's sender with the same message.
    /// </summary>
    public SimpleEchoActor()
    {
        ReceiveAny(msg =>
        {
            Sender.Tell(msg);
        });
    }

    /// <summary>
    /// Returns a <see cref="Props"/> object that can be used to create an <see cref="SimpleEchoActor"/>.
    /// The <see cref="SimpleEchoActor"/> echoes whatever is sent to it, to the `Sender`.
    /// </summary>
    /// <returns>Props for creating the simple echo actor.</returns>
    public static Props Props()
    {
        return Actor.Props.Create(() => new SimpleEchoActor());
    }
}
