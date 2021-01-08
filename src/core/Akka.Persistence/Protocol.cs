//-----------------------------------------------------------------------
// <copyright file="Protocol.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.Persistence
{
    /// <summary>
    /// Marker interface for internal persistence extension messages.
    /// 
    /// Helps persistence plugin developers to differentiate
    /// internal persistence extension messages from their custom plugin messages.
    /// 
    /// Journal messages need not be serialization verified as the Journal Actor
    /// should always be a local Actor (and serialization is performed by plugins).
    /// One notable exception to this is the shared journal used for testing.
    /// </summary>
    public interface IPersistenceMessage : INoSerializationVerificationNeeded
    {

    }
}
