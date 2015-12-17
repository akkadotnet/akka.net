using System;
using Akka.Actor;
using Akka.Cluster.Sharding;

namespace ClusterSharding.Node
{
    public class Printer : ReceiveActor
    {
        #region Message extractor

        public sealed class MessageExtractor : IMessageExtractor
        {
            public string EntityId(object message)
            {
                var env = message as Env;
                return env != null ? env.EntityId.ToString() : null;
            }

            public object EntityMessage(object message)
            {
                var env = message as Env;
                return env != null ? env.Message : null;
            }

            public string ShardId(object message)
            {
                var env = message as Env;
                return env != null ? env.ShardId.ToString() : null;
            }
        }

        #endregion

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