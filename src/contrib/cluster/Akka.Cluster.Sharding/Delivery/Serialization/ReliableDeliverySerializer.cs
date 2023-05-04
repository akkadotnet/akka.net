// -----------------------------------------------------------------------
//  <copyright file="ReliableDeliverySerializer.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------
#nullable enable
using System;
using Akka.Actor;
using Akka.Serialization;

namespace Akka.Cluster.Sharding.Delivery.Serialization;

/// <summary>
/// INTERNAL API
/// </summary>
internal sealed class ReliableDeliverySerializer : SerializerWithStringManifest
{
    
    
    public ReliableDeliverySerializer(ExtendedActorSystem system) : base(system)
    {
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
        throw new NotImplementedException();
    }
}