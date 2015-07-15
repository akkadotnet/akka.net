//-----------------------------------------------------------------------
// <copyright file="InetAddressDnsProvider.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.IO
{
    public class InetAddressDnsProvider : IDnsProvider
    {
        private readonly DnsBase _cache = new SimpleDnsCache();

        public DnsBase Cache { get { return _cache; }}
        public Type ActorClass { get { return typeof (InetAddressDnsResolver); } }
        public Type ManagerClass { get { return typeof (SimpleDnsManager); } }
    }
}
