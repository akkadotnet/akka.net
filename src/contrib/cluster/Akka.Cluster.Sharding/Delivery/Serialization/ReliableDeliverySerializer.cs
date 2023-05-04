// -----------------------------------------------------------------------
//  <copyright file="ReliableDeliverySerializer.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------
#nullable enable
using System;
using Akka.Actor;
using Akka.Delivery;
using Akka.Remote.Serialization;
using Akka.Serialization;

namespace Akka.Cluster.Sharding.Delivery.Serialization;

/// <summary>
/// INTERNAL API
/// </summary>
internal sealed class ReliableDeliverySerializer : SerializerWithStringManifest
{
    private readonly WrappedPayloadSupport _payloadSupport;
    private const string SequencedMessageManifest = "a";
    private const string AckManifest = "b";
    private const string RequestManifest = "c";
    private const string ResendManifest = "d";
    private const string RegisterConsumerManifest = "e";
    
    // durable queue manifests
    private const string DurableQueueMessageSentManifest = "f";
    private const string DurableQueueConfirmedManifest = "g";
    private const string DurableQueueStateManifest = "h";
    private const string DurableQueueCleanupManifest = "i";

    public ReliableDeliverySerializer(ExtendedActorSystem system) : base(system)
    {
        _payloadSupport = new WrappedPayloadSupport(system);
    }

    public override byte[] ToBinary(object obj)
    {
        throw new NotImplementedException();
    }

    public override object FromBinary(byte[] bytes, string manifest)
    {
        throw new NotImplementedException();
    }

    public override string Manifest(object o)
    {
        switch (o)
        {
            case ConsumerController.SequencedMessage<_>
        }
    }
}