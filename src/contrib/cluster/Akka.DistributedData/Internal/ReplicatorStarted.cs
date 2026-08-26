//-----------------------------------------------------------------------
// <copyright file="ReplicatorStarted.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.DistributedData.Internal
{
    internal sealed record ReplicatorStarted(IActorRef Replicator)
        : INoSerializationVerificationNeeded;
}
