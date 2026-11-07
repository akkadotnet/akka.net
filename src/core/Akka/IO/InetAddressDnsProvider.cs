//-----------------------------------------------------------------------
// <copyright file="InetAddressDnsProvider.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.IO
{
    /// <summary>
    /// Provides the built-in DNS cache, resolver, and manager based on .NET address resolution.
    /// </summary>
    public class InetAddressDnsProvider : IDnsProvider
    {
        private readonly DnsBase _cache = new SimpleDnsCache();

        /// <summary>
        /// Gets the simple DNS cache used by this provider.
        /// </summary>
        public DnsBase Cache { get { return _cache; }}
        /// <summary>
        /// Gets the built-in resolver actor type.
        /// </summary>
        public Type ActorClass { get { return typeof (InetAddressDnsResolver); } }
        /// <summary>
        /// Gets the simple DNS manager actor type.
        /// </summary>
        public Type ManagerClass { get { return typeof (SimpleDnsManager); } }
    }
}
