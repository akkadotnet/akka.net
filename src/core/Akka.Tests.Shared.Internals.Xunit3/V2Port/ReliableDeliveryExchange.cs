//-----------------------------------------------------------------------
// <copyright file="ReliableDeliveryExchange.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Delivery;
using Akka.Util;

// ReSharper disable once CheckNamespace
namespace Akka.TestKit;

/// <summary>The messages sent and received by <see cref="ReliableDeliveryExchange.RunAsync{T}"/>.</summary>
/// <typeparam name="T">The user message type.</typeparam>
public sealed class ReliableDeliveryExchangeResult<T>
{
    internal ReliableDeliveryExchangeResult(IReadOnlyList<T> sent, IReadOnlyList<T> received)
    {
        Sent = sent;
        Received = received;
    }

    /// <summary>What the producer sent, in order.</summary>
    public IReadOnlyList<T> Sent { get; }

    /// <summary>What the consumer side received, in order.</summary>
    public IReadOnlyList<T> Received { get; }
}

/// <summary>
/// A live <see cref="ProducerController{T}"/> exchange with a probe standing in for the consumer side. Run it on a
/// system with <c>akka.actor.serialize-messages = on</c> and every <c>SequencedMessage</c> the producer controller sends
/// and every <c>Request</c> the probe answers with goes through the serializer bound to Akka.Delivery's wire types, so it
/// shows the serializer works in the protocol it was written for, not only message by message.
/// </summary>
/// <remarks>
/// <para>
/// The consumer side is a probe because a real <see cref="ConsumerController{T}"/> hands its <c>Delivery</c> to the
/// application through a local <c>Tell</c>, and <c>Delivery</c> is not a wire message: under
/// <c>serialize-messages = on</c> that hand-off fails in the serializer.
/// </para>
/// <para>
/// Akka.Delivery's wire types (<c>IDeliverySerializable</c>) are bound to a serializer by Akka.Cluster's config, so the
/// system needs <c>ClusterConfigFactory.Default()</c> in its config; a plain local provider is enough. Chunked messages
/// (<c>ChunkLargeMessagesBytes</c>) are not supported by the helper. A serializer that can't carry the protocol shows up
/// as a timeout naming the step that stalled.
/// </para>
/// </remarks>
public static class ReliableDeliveryExchange
{
    private static int _counter;

    /// <summary>The window the probe grants the producer with each <c>Request</c>.</summary>
    private const int RequestWindow = 20;

    /// <summary>
    /// Sends <paramref name="messages"/> through a producer controller, one at a time, answering each
    /// <c>SequencedMessage</c> with the <c>Request</c> a consumer controller would send.
    /// </summary>
    /// <typeparam name="T">The user message type; it has to serialize on <paramref name="testKit"/>'s system.</typeparam>
    /// <param name="testKit">The spec: its system runs the controller, and its probes watch the two ends.</param>
    /// <param name="messages">What to send.</param>
    /// <param name="timeout">How long each step may take; 10 seconds when null.</param>
    /// <param name="cancellationToken">Cancels the exchange.</param>
    public static async Task<ReliableDeliveryExchangeResult<T>> RunAsync<T>(
        TestKitBase testKit, IReadOnlyList<T> messages, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var stepTimeout = timeout ?? TimeSpan.FromSeconds(10);
        var system = testKit.Sys;
        var id = Interlocked.Increment(ref _counter);

        var producerProbe = testKit.CreateTestProbe($"delivery-exchange-producer-probe-{id}");
        var consumerProbe = testKit.CreateTestProbe($"delivery-exchange-consumer-probe-{id}");
        var producerController = system.ActorOf(
            ProducerController.Create<T>(system, $"delivery-exchange-{id}", Option<Props>.None), $"delivery-exchange-pc-{id}");

        try
        {
            producerController.Tell(new ProducerController.Start<T>(producerProbe.Ref));
            producerController.Tell(new ProducerController.RegisterConsumer<T>(consumerProbe.Ref));

            var received = new List<T>(messages.Count);
            foreach (var message in messages)
            {
                var next = await producerProbe.ExpectMsgAsync<ProducerController.RequestNext<T>>(stepTimeout, cancellationToken: cancellationToken);
                next.SendNextTo.Tell(message);

                var sequenced = await consumerProbe.ExpectMsgAsync<ConsumerController.SequencedMessage<T>>(stepTimeout, cancellationToken: cancellationToken);
                if (!sequenced.Message.IsMessage)
                    throw new NotSupportedException("ReliableDeliveryExchange does not support chunked messages");
                received.Add(sequenced.Message.Message!);

                producerController.Tell(
                    new ProducerController.Request(sequenced.SeqNr, sequenced.SeqNr + RequestWindow, supportResend: true, viaTimeout: false),
                    consumerProbe.Ref);
            }

            return new ReliableDeliveryExchangeResult<T>(messages, received);
        }
        finally
        {
            system.Stop(producerController);
        }
    }
}
