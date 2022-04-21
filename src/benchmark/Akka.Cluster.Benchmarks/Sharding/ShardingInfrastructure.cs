//-----------------------------------------------------------------------
// <copyright file="ShardingInfrastructure.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Sharding;
using Akka.Configuration;
using Akka.Util.Internal;

namespace Akka.Cluster.Benchmarks.Sharding
{
    public sealed class ShardedEntityActor : ReceiveActor
    {
        public sealed class Resolve
        {
            public static readonly Resolve Instance = new Resolve();
            private Resolve(){}
        }

        public sealed class ResolveResp
        {
            public ResolveResp(string entityId, Address addr)
            {
                EntityId = entityId;
                Addr = addr;
            }

            public string EntityId { get; }
            
            public Address Addr { get; }
        }
        
        public ShardedEntityActor()
        {
            Receive<ShardingEnvelope>(e =>
            {
                Sender.Tell(new ResolveResp(e.EntityId, Cluster.Get(Context.System).SelfAddress));
            });
            
            ReceiveAny(_ => Sender.Tell(_));
        }
    }



    public sealed class ShardedProxyEntityActor : ReceiveActor, IWithUnboundedStash
    {
        private IActorRef _shardRegion;
        private IActorRef _sender;

        

      

        public ShardedProxyEntityActor(IActorRef shardRegion)
        {
            _shardRegion = shardRegion;
            WaitRequest();
        }

        public void WaitRequest()
        {
           
            Receive<SendShardedMessage>(e =>
            {
                _sender = Sender;
                _shardRegion.Tell(e.Message);
                Become(WaitResult);

            });

            ReceiveAny(x => {
                Sender.Tell(x);
                });

            
        }


        public void WaitResult()
        {
            Receive<ShardedMessage>((msg) => {
                _sender.Tell(msg);
                Stash.UnstashAll();
                Become(WaitRequest);
            });

            ReceiveAny(msg =>
                Stash.Stash()
            );


        }

        public IStash Stash { get; set; }

    }



    public sealed class BulkSendActor : ReceiveActor
    {
        public sealed class BeginSend
        {
            public BeginSend(ShardedMessage msg, IActorRef target, int batchSize)
            {
                Msg = msg;
                Target = target;
                BatchSize = batchSize;
            }

            public ShardedMessage Msg { get; }
            
            public IActorRef Target { get; }
            
            public int BatchSize { get; }
        }
        
        private int _remaining;

        private readonly TaskCompletionSource<bool> _tcs;

        public BulkSendActor(TaskCompletionSource<bool> tcs, int remaining)
        {
            _tcs = tcs;
            _remaining = remaining;

            Receive<BeginSend>(b =>
            {
                for (var i = 0; i < b.BatchSize; i++)
                {
                    b.Target.Tell(b.Msg);
                }
            });

            Receive<ShardedMessage>(s =>
            {
                if (--remaining > 0)
                {
                    Sender.Tell(s);
                }
                else
                {
                    Context.Stop(Self); // shut ourselves down
                    _tcs.TrySetResult(true);
                }
            });
        }
    }
    
    public sealed class ShardedMessage
    {
        public ShardedMessage(string entityId, int message)
        {
            EntityId = entityId;
            Message = message;
        }

        public string EntityId { get; }
            
        public int Message { get; }
    }



    public sealed class SendShardedMessage
    {
        public SendShardedMessage(string entityId, ShardedMessage message)
        {
            EntityId = entityId;
            Message = message;
        }

        public string EntityId { get; }

        public ShardedMessage Message { get; }
    }


    /// <summary>
    /// Use a default <see cref="IMessageExtractor"/> even though it takes extra work to setup the benchmark
    /// </summary>
    public sealed class ShardMessageExtractor : HashCodeMessageExtractor
    {
        /// <summary>
        /// We only ever run with a maximum of two nodes, so ~10 shards per node
        /// </summary>
        public ShardMessageExtractor(int shardCount = 20) : base(shardCount)
        {
        }

        public override string EntityId(object message)
        {
            if(message is ShardedMessage sharded)
            {
                return sharded.EntityId;
            }

            if (message is ShardingEnvelope e)
            {
                return e.EntityId;
            }

            return null;
        }
    }

    public static class ShardingHelper
    {
        public static AtomicCounter DbId = new AtomicCounter(0);

        internal static string BoolToToggle(bool val)
        {
            return val ? "on" : "off";
        }
        
        public static Config CreatePersistenceConfig(bool rememberEntities = false)
        {
            var connectionString =
                "Filename=file:memdb-journal-" + DbId.IncrementAndGet() + ".db;Mode=Memory;Cache=Shared";
            var config = $@"
                akka.actor.provider = cluster
                akka.remote.dot-netty.tcp.port = 0
                akka.cluster.sharding.state-store-mode=persistence
                akka.cluster.sharding.remember-entities = {BoolToToggle(rememberEntities)}
                akka.persistence.journal.plugin = ""akka.persistence.journal.sqlite""
                akka.persistence.journal.sqlite {{
                    class = ""Akka.Persistence.Sqlite.Journal.SqliteJournal, Akka.Persistence.Sqlite""
                    auto-initialize = on
                    connection-string = ""{connectionString}""
                }}
                akka.persistence.snapshot-store {{
                    plugin = ""akka.persistence.snapshot-store.sqlite""
                    sqlite {{
                        class = ""Akka.Persistence.Sqlite.Snapshot.SqliteSnapshotStore, Akka.Persistence.Sqlite""
                        auto-initialize = on
                        connection-string = ""{connectionString}""
                    }}
                }}";

            return config;
        }

        public static Config CreateDDataConfig(bool rememberEntities = false)
        {
            var config = $@"
                akka.actor.provider = cluster
                akka.remote.dot-netty.tcp.port = 0
                akka.cluster.sharding.state-store-mode=ddata
                akka.cluster.sharding.remember-entities = {BoolToToggle(rememberEntities)}";

            return config;
        }

        public static IActorRef StartShardRegion(ActorSystem system, string entityName = "entities")
        {
            var props = Props.Create(() => new ShardedEntityActor());
            var sharding = ClusterSharding.Get(system);
            return sharding.Start(entityName, s => props, ClusterShardingSettings.Create(system),
                new ShardMessageExtractor());
        }
    }
}