#region copyright
// -----------------------------------------------------------------------
//  <copyright file="LmdbDurableStore.cs" company="Akka.NET project">
//      Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2017 Akka.NET project <https://github.com/akkadotnet>
//  </copyright>
// -----------------------------------------------------------------------
#endregion

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Text;
using Akka.Actor;
using Akka.Configuration;
using Akka.DistributedData.Internal;
using Akka.Event;
using Akka.IO;
using Akka.Serialization;
using Google.ProtocolBuffers;
using LightningDB;

namespace Akka.DistributedData.Durable
{
    /// <summary>
    /// An actor implementing the durable store for the Distributed Data <see cref="Replicator"/>
    /// has to implement the protocol with the messages defined here.
    /// 
    /// At startup the <see cref="Replicator"/> creates the durable store actor and sends the
    /// <see cref="Load"/> message to it. It must then reply with 0 or more <see cref="LoadData"/> messages
    /// followed by one <see cref="LoadAllCompleted"/> message to the `sender` (the <see cref="Replicator"/>).
    /// 
    /// If the <see cref="LoadAll"/> fails it can throw <see cref="LoadFailed"/> and the <see cref="Replicator"/> supervisor
    /// will stop itself and the durable store.
    /// 
    /// When the <see cref="Replicator"/> needs to store a value it sends a <see cref="Store"/> message
    /// to the durable store actor, which must then reply with the <see cref="StoreReply.SuccessMessage"/> or
    /// <see cref="StoreReply.FailureMessage"/> to the <see cref="StoreReply.ReplyTo"/>.
    /// </summary>
    public sealed class LmdbDurableStore : ReceiveActor
    {
        private sealed class WriteBehind
        {
            public static readonly WriteBehind Instance = new WriteBehind();
            private WriteBehind() { }
        }

        public static Actor.Props Props(Config config) => Actor.Props.Create(() => new LmdbDurableStore(config));

        private readonly TimeSpan _writeBehindInterval;
        private readonly Dictionary<string, IReplicatedData> _pending = new Dictionary<string, IReplicatedData>();
        private readonly LightningEnvironment _environment;
        private readonly LightningDatabase _db;
        private readonly ILoggingAdapter _log;
        private readonly Serializer _serializer;
        private ByteBuffer _keyBuffer;
        private ByteBuffer _valueBuffer;

        public LmdbDurableStore(Config config)
        {
            _log = Context.GetLogger();
            _writeBehindInterval = config.GetString("lmdb.write-behind-interval") == "off" 
                ? TimeSpan.Zero : config.GetTimeSpan("lmdb.write-behind-interval");

            var mapSize = config.GetByteSize("lmdb.map-size");
            var dirPath = config.GetString("lmdb.dir");
            if (dirPath.EndsWith("ddata"))
            {
                dirPath = $"path-{Context.System.Name}-{Self.Path.Parent.Name}-{Cluster.Cluster.Get(Context.System).SelfAddress.Port.Value}";
            }

            if (!Directory.Exists(dirPath))
            {
                Directory.CreateDirectory(dirPath);
            }

            _environment = new LightningEnvironment(dirPath, new EnvironmentConfiguration
            {
                MapSize = mapSize.Value,
                MaxDatabases = 1
            });
            _environment.Open(EnvironmentOpenFlags.NoLock);

            using (var tx = _environment.BeginTransaction())
            {
                _db = tx.OpenDatabase("ddata", new DatabaseConfiguration {Flags = DatabaseOpenFlags.Create});
            }

            _keyBuffer = ByteBuffer.Allocate(1024);
            _valueBuffer = ByteBuffer.Allocate(100 * 1014);
            _serializer = Context.System.Serialization.FindSerializerForType(typeof(DurableDataEnvelope));

            Init();
        }

        protected override void PostStop()
        {
            base.PostStop();
            DoWriteBehind();
            _db.Dispose();
            _environment.Dispose();
        }

        protected override void PostRestart(Exception reason)
        {
            base.PostRestart(reason);
            // Load is only done on first start, not on restart
            Become(Active);
        }

        private void Active()
        {
            Receive<Store>(store =>
            {
                var reply = store.Reply;

                try
                {
                    if (_writeBehindInterval == TimeSpan.Zero)
                    {
                        DbPut(null, store.Key, store.Data);
                    }
                    else
                    {
                        if (_pending.Count > 0)
                            Context.System.Scheduler.ScheduleTellOnce(_writeBehindInterval, Self, WriteBehind.Instance, ActorRefs.NoSender);
                        _pending[store.Key] = store.Data;
                    }

                    reply?.ReplyTo.Tell(reply.SuccessMessage);
                }
                catch (Exception cause)
                {
                    _log.Error(cause, "Failed to store [{0}]", store.Key);
                    reply?.ReplyTo.Tell(reply.FailureMessage);
                }
            });
            Receive<WriteBehind>(_ => DoWriteBehind());
        }

        private void Init()
        {
            Receive<LoadAll>(loadAll =>
            {
                var t0 = System.DateTime.UtcNow;
                using (var tx = _environment.BeginTransaction(TransactionBeginFlags.ReadOnly))
                {
                    LightningCursor cursor = null;
                    try
                    {
                        var n = 0;
                        var builder = ImmutableDictionary<string, IReplicatedData>.Empty.ToBuilder();
                        cursor = tx.CreateCursor(_db);
                        foreach (var entry in cursor)
                        {
                            n++;
                            var key = Encoding.UTF8.GetString(entry.Key);
                            var envelope = (DurableDataEnvelope)_serializer.FromBinary(entry.Value, typeof(DurableDataEnvelope));
                            builder.Add(new KeyValuePair<string, IReplicatedData>(key, envelope.Data));
                        }
                        //TODO

                        Sender.Tell(LoadAllCompleted.Instance);

                        if (_log.IsDebugEnabled)
                            _log.Debug("Load all of [{0}] entries took [{1}]", n, DateTime.UtcNow - t0);

                        Become(Active);
                    }
                    catch (Exception e)
                    {
                        //TODO
                    }
                    finally
                    {
                        cursor?.Dispose();
                    }
                }
            });
        }

        private void DbPut(LightningTransaction tx, string key, IReplicatedData data)
        {
            try
            {
                Encoding.UTF8.GetBytes(key, 0, key.Length, _keyBuffer.Array(), 0);
                var value = _serializer.ToBinary(new DurableDataEnvelope(data));
                _valueBuffer.Put(value);
                tx.Put(_db,);
            }
            finally
            {
                
            }
        }

        private void DoWriteBehind()
        {
            if (_pending.Count > 0)
            {
                var t0 = DateTime.UtcNow;
                using (var tx = _environment.BeginTransaction())
                {
                    try
                    {

                    }
                    catch (Exception cause)
                    {

                    }
                    finally
                    {
                        _pending.Clear();
                    }
                }
            }
        }
    }
}