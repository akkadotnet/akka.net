//-----------------------------------------------------------------------
// <copyright file="DnsProvider.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.IO
{
    public interface IDnsProvider
    {
        DnsBase Cache { get; }
        Type ActorClass { get; }
        Type ManagerClass { get; }
    }
}
