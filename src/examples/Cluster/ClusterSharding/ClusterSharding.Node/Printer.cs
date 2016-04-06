//-----------------------------------------------------------------------
// <copyright file="Printer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Cluster.Sharding;

namespace ClusterSharding.Node
{
    public class Printer : ReceiveActor
    {
        #region Messages
        
        public sealed class Env
        {
            public readonly int ShardId;
            public readonly int EntityId;
            public readonly string Message;

            public Env(int shardId, int entityId, string message)
            {
                ShardId = shardId;
                EntityId = entityId;
                Message = message;
            }
        }

        #endregion
        
        public Printer()
        {
            Context.SetReceiveTimeout(TimeSpan.FromMinutes(2));
            Receive<string>(message => Console.WriteLine("{0} received message '{1}'", Self, message));
        }
        
    }
}