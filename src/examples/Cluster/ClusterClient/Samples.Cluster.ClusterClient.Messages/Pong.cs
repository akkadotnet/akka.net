//-----------------------------------------------------------------------
// <copyright file="Pong.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Samples.Cluster.ClusterClient.Messages
{
    public class Pong
    {
        public Pong(string rsp, Address replyAddr)
        {
            Rsp = rsp;
            ReplyAddr = replyAddr;
        }

        public string Rsp { get; }

        public Address ReplyAddr { get; }
    }
}
