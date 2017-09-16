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
using Akka.DistributedData.Durable;
using Akka.Event;
using Akka.Serialization;
using LightningDB;

namespace Akka.DistributedData.LightningDB
{
    /// <summary>
    /// An actor implementing the durable store for the Distributed Data <see cref="Replicator"/>
    /// has to implement the protocol with the messages defined here.
    /// 
    /// At startup the <see cref="Replicator"/> creates the durable store actor and sends the
    /// <see cref="LoadAll"/> message to it. It must then reply with 0 or more <see cref="LoadData"/> messages
    /// followed by one <see cref="LoadAllCompleted"/> message to the <see cref="IActorContext.Sender"/> (the <see cref="Replicator"/>).
    /// 
    /// If the <see cref="LoadAll"/> fails it can throw <see cref="LoadFailedException"/> and the <see cref="Replicator"/> supervisor
    /// will stop itself and the durable store.
    /// 
    /// When the <see cref="Replicator"/> needs to store a value it sends a <see cref="Store"/> message
    /// to the durable store actor, which must then reply with the <see cref="StoreReply.SuccessMessage"/> or
    /// <see cref="StoreReply.FailureMessage"/> to the <see cref="StoreReply.ReplyTo"/>.
    /// </summary>
    public sealed class LmdbDurableStore : ReceiveActor
    {
        public const string DatabaseName = "ddata";
        private sealed class WriteBehind
        {
            public static readonly WriteBehind Instance = new WriteBehind();
            private WriteBehind() { }
        }

        public static Actor.Props Props(Config config) => Actor.Props.Create(() => new LmdbDurableStore(config));

        private readonly TimeSpan _writeBehindInterval;
        private readonly Dictionary<string, DurableDataEnvelope> _pending = new Dictionary<string, DurableDataEnvelope>();
        private readonly LightningEnvironment _environment;
        private readonly ILoggingAdapter _log;
        private readonly Serializer _serializer;

        public LmdbDurableStore(Config config)
        {
            config = config.GetConfig("lmdb");
            if (config == null) throw new ArgumentException("Couldn't find config for LMDB durable store. Default path: `akka.cluster.distributed-data.durable.lmdb`");

            _log = Context.GetLogger();

            _writeBehindInterval = config.GetString("write-behind-interval") == "off" 
                ? TimeSpan.Zero : config.GetTimeSpan("write-behind-interval");

            var mapSize = config.GetByteSize("map-size");
            var dirPath = config.GetString("dir");
            if (dirPath.EndsWith("ddata"))
            {
                dirPath = $"path-{Context.System.Name}-{Self.Path.Parent.Name}-{Cluster.Cluster.Get(Context.System).SelfAddress.Port}";
            }

            if (!Directory.Exists(dirPath))
            {
                Directory.CreateDirectory(dirPath);
            }

            _environment = new LightningEnvironment(dirPath, new EnvironmentConfiguration
            {
                MapSize = mapSize ?? (100 * 1024 * 1024),
                MaxDatabases = 1
            });
            _environment.Open(EnvironmentOpenFlags.NoLock);

            using (var tx = _environment.BeginTransaction())
            using (var db = tx.OpenDatabase(configuration: new DatabaseConfiguration { Flags = DatabaseOpenFlags.Create }))
            {
                // just create
            }
            
            _serializer = Context.System.Serialization.FindSerializerForType(typeof(DurableDataEnvelope));

            Init();
        }

        protected override void PostStop()
        {
            base.PostStop();
            DoWriteBehind();
            //_db.Dispose();
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
                        using (var tx = _environment.BeginTransaction())
                        using (var db = tx.OpenDatabase())
                        {
                            DbPut(tx, db, store.Key, store.Data);
                            tx.Commit();
                        }
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
                using (var db = tx.OpenDatabase())
                using (var cursor = tx.CreateCursor(db))
                {
                    try
                    {
                        var n = 0;
                        var builder = ImmutableDictionary<string, DurableDataEnvelope>.Empty.ToBuilder();
                        foreach (var entry in cursor)
                        {
                            n++;
                            var key = Encoding.UTF8.GetString(entry.Key);
                            var envelope = (DurableDataEnvelope)_serializer.FromBinary(entry.Value, typeof(DurableDataEnvelope));
                            builder.Add(new KeyValuePair<string, DurableDataEnvelope>(key, envelope));
                        }

                        if (builder.Count > 0)
                        {
                            var loadData = new LoadData(builder.ToImmutable());
                            Sender.Tell(loadData);
                        }

                        Sender.Tell(LoadAllCompleted.Instance);

                        if (_log.IsDebugEnabled)
                            _log.Debug("Load all of [{0}] entries took [{1}]", n, DateTime.UtcNow - t0);

                        Become(Active);
                    }
                    catch (Exception e)
                    {
                        throw new LoadFailedException("failed to load durable distributed-data", e);
                    }
                }
            });
        }

        private void DbPut(LightningTransaction tx, LightningDatabase db, string key, DurableDataEnvelope data)
        {
            var byteKey = Encoding.UTF8.GetBytes(key);
            var byteValue = _serializer.ToBinary(data);
            tx.Put(db, byteKey, byteValue);
        }

        private void DoWriteBehind()
        {
            if (_pending.Count > 0)
            {
                var t0 = DateTime.UtcNow;
                using (var tx = _environment.BeginTransaction())
                using (var db = tx.OpenDatabase())
                {
                    try
                    {
                        foreach (var entry in _pending)
                        {
                            DbPut(tx, db, entry.Key, entry.Value);
                        }
                        tx.Commit();

                        if (_log.IsDebugEnabled)
                        {
                            _log.Debug("store and commit of [{0}] entries took {1} ms", _pending.Count, (DateTime.UtcNow - t0).TotalMilliseconds);
                        }
                    }
                    catch (Exception cause)
                    {
                        _log.Error(cause, "failed to store [{0}]", string.Join(", ", _pending.Keys));
                        tx.Abort();
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