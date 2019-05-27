//-----------------------------------------------------------------------
// <copyright file="Ping.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Samples.Cluster.ClusterClient.Messages
{
    public class Ping
    {
        public Ping(string msg)
        {
            Msg = msg;
        }

        public string Msg { get; }
    }
}
