//-----------------------------------------------------------------------
// <copyright file="DnsProvider.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.IO
{
    /// <summary>
    /// Supplies the cache, resolver actor type, and manager actor type used by an Akka.IO DNS extension.
    /// </summary>
    public interface IDnsProvider
    {
        /// <summary>
        /// Gets the DNS cache shared by the resolver and manager actors.
        /// </summary>
        DnsBase Cache { get; }
        /// <summary>
        /// Gets the actor type used to resolve DNS queries.
        /// </summary>
        Type ActorClass { get; }
        /// <summary>
        /// Gets the actor type used to manage DNS resolution and cache maintenance.
        /// </summary>
        Type ManagerClass { get; }
    }
}
