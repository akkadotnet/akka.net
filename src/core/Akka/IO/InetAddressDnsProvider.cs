// -----------------------------------------------------------------------
//  <copyright file="InetAddressDnsProvider.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;

namespace Akka.IO;

/// <summary>
///     TBD
/// </summary>
public class InetAddressDnsProvider : IDnsProvider
{
    /// <summary>
    ///     TBD
    /// </summary>
    public DnsBase Cache { get; } = new SimpleDnsCache();

    /// <summary>
    ///     TBD
    /// </summary>
    public Type ActorClass => typeof(InetAddressDnsResolver);

    /// <summary>
    ///     TBD
    /// </summary>
    public Type ManagerClass => typeof(SimpleDnsManager);
}