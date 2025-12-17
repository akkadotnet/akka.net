//-----------------------------------------------------------------------
// <copyright file="IDeliverySerializable.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Annotations;

namespace Akka.Delivery.Internal;

/// <summary>
///     INTERNAL API
/// </summary>
/// <remarks>
///     Marker interface for messages that are serialized by DeliverySerializer
/// </remarks>
[InternalApi]
public interface IDeliverySerializable
{
}
