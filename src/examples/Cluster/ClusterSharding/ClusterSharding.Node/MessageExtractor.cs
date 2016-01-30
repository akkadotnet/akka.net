//-----------------------------------------------------------------------
// <copyright file="MessageExtractor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Cluster.Sharding;

namespace ClusterSharding.Node
{
    public sealed class MessageExtractor : IMessageExtractor
    {
        public string EntityId(object message)
        {
            var env = message as Printer.Env;
            return env != null ? env.EntityId.ToString() : null;
        }

        public object EntityMessage(object message)
        {
            var env = message as Printer.Env;
            return env != null ? env.Message : null;
        }

        public string ShardId(object message)
        {
            var env = message as Printer.Env;
            return env != null ? env.ShardId.ToString() : null;
        }
    }
}