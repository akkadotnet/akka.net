//-----------------------------------------------------------------------
// <copyright file="DnsProvider.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.IO
{
    /// <summary>
    /// TBD
    /// </summary>
    public interface IDnsProvider
    {
        /// <summary>
        /// TBD
        /// </summary>
        DnsBase Cache { get; }
        /// <summary>
        /// TBD
        /// </summary>
        Type ActorClass { get; }
        /// <summary>
        /// TBD
        /// </summary>
        Type ManagerClass { get; }
    }
}
