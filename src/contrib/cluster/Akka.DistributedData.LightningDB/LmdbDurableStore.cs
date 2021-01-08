//-----------------------------------------------------------------------
// <copyright file="LmdbDurableStore.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

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
using Akka.DistributedData.Internal;
using LightningDB;
using System.Diagnostics;
using System.Linq;

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
        public static Actor.Props Props(Config config) => Actor.Props.Create(() => new LmdbDurableStore(config));

        public const string DatabaseName = "ddata";

        private sealed class WriteBehind
        {
            public static readonly WriteBehind Instance = new WriteBehind();
            private WriteBehind() { }
        }

        private readonly Config _config;
        private readonly Akka.Serialization.Serialization _serialization;
        private readonly SerializerWithStringManifest _serializer;
        private readonly string _manifest;

        private readonly TimeSpan _writeBehindInterval;
        private readonly string _dir;

        private readonly Dictionary<string, DurableDataEnvelope> _pending = new Dictionary<string, DurableDataEnvelope>();
        private readonly ILoggingAdapter _log;

        private (LightningEnvironment env, LightningDatabase db, bool initialized) _lmdb;
        // Lazy init
        private (LightningEnvironment env, LightningDatabase db, bool initialized) Lmdb
        {
            get
            {
                if (_lmdb.initialized)
                    return _lmdb;

                var t0 = Stopwatch.StartNew();
                _log.Info($"Using durable data in LMDB directory [{_dir}]");

                if (!Directory.Exists(_dir))
                    Directory.CreateDirectory(_dir);

                var mapSize = _config.GetByteSize("map-size", 100 * 1024 * 1024);
                var env = new LightningEnvironment(_dir)
                {
                    MapSize = mapSize.Value,
                    MaxDatabases = 1
                };
                env.Open(EnvironmentOpenFlags.NoLock);

                using (var tx = env.BeginTransaction())
                {
                    var db = tx.OpenDatabase(DatabaseName, new DatabaseConfiguration
                    {
                        Flags = DatabaseOpenFlags.Create
                    });
                    tx.Commit();

                    t0.Stop();
                    if (_log.IsDebugEnabled)
                        _log.Debug($"Init of LMDB in directory [{_dir}] took [{t0.ElapsedMilliseconds} ms]");

                    _lmdb = (env, db, true);
                    return _lmdb;
                }
            }
        }

        public bool IsDbInitialized => _lmdb.initialized;

        public LmdbDurableStore(Config config)
        {
            _config = config.GetConfig("lmdb");
            if (_config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<LmdbDurableStore>("akka.cluster.distributed-data.durable.lmdb");

            _log = Context.GetLogger();

            _serialization = Context.System.Serialization;
            _serializer = (SerializerWithStringManifest) _serialization.FindSerializerForType(typeof(DurableDataEnvelope));
            _manifest = _serializer.Manifest(new DurableDataEnvelope(GCounter.Empty));

            var useWriteBehind = _config.GetString("write-behind-interval", "").ToLowerInvariant();
            _writeBehindInterval = 
                useWriteBehind == "off" ||
                useWriteBehind == "false" ||
                useWriteBehind == "no" ? 
                    TimeSpan.Zero :
                    _config.GetTimeSpan("write-behind-interval");

            var path = _config.GetString("dir");
            _dir = path.EndsWith(DatabaseName)
                ? Path.GetFullPath($"{path}-{Context.System.Name}-{Self.Path.Parent.Name}-{Cluster.Cluster.Get(Context.System).SelfAddress.Port}")
                : Path.GetFullPath(path);

            Init();
        }

        protected override void PostRestart(Exception reason)
        {
            base.PostRestart(reason);
            // Load is only done on first start, not on restart
            Become(Active);
        }

        protected override void PostStop()
        {
            base.PostStop();
            DoWriteBehind();

            if(IsDbInitialized)
            {
                var (env, db, _) = Lmdb;
                try { db?.Dispose(); } catch { }
                try { env?.Dispose(); } catch { }
            }
        }

        private void Active()
        {
            Receive<Store>(store =>
            {
                try
                {
                    var l = Lmdb; // init
                    if (_writeBehindInterval == TimeSpan.Zero)
                    {
                        using (var tx = l.env.BeginTransaction())
                        {
                            DbPut(tx, store.Key, store.Data);
                            tx.Commit();
                        }
                    }
                    else
                    {
                        if (_pending.Count > 0)
                            Context.System.Scheduler.ScheduleTellOnce(_writeBehindInterval, Self, WriteBehind.Instance, ActorRefs.NoSender);
                        _pending[store.Key] = store.Data;
                    }

                    store.Reply?.ReplyTo.Tell(store.Reply.SuccessMessage);
                }
                catch (Exception cause)
                {
                    _log.Error(cause, "Failed to store [{0}]:{1}", store.Key, cause);
                    store.Reply?.ReplyTo.Tell(store.Reply.FailureMessage);
                }
            });

            Receive<WriteBehind>(_ => DoWriteBehind());
        }

        private void Init()
        {
            Receive<LoadAll>(loadAll =>
            {
                if(_dir.Length == 0 || !Directory.Exists(_dir))
                {
                    // no files to load
                    Sender.Tell(LoadAllCompleted.Instance);
                    Become(Active);
                    return;
                }

                var (environment, db, _) = Lmdb;
                var t0 = Stopwatch.StartNew();
                using (var tx = environment.BeginTransaction(TransactionBeginFlags.ReadOnly))
                using (var cursor = tx.CreateCursor(db))
                {
                    try
                    {
                        var data = cursor.AsEnumerable().Select((x, i)
                            => {
                                var (key, value) = x;
                                return new KeyValuePair<string, DurableDataEnvelope>(
                                    Encoding.UTF8.GetString(key.CopyToNewArray()),
                                    (DurableDataEnvelope)_serializer.FromBinary(value.CopyToNewArray(), _manifest));
                            }).ToImmutableDictionary();

                        if (data.Count > 0)
                        {
                            var loadData = new LoadData(data);
                            Sender.Tell(loadData);
                        }

                        Sender.Tell(LoadAllCompleted.Instance);

                        t0.Stop();
                        if (_log.IsDebugEnabled)
                            _log.Debug($"Load all of [{data.Count}] entries took [{t0.ElapsedMilliseconds}]");

                        Become(Active);
                    }
                    catch (Exception e)
                    {
                        if (t0.IsRunning) t0.Stop();
                        throw new LoadFailedException("failed to load durable distributed-data", e);
                    }
                }
            });
        }

        private void DbPut(LightningTransaction tx, string key, DurableDataEnvelope data)
        {
            var byteKey = Encoding.UTF8.GetBytes(key);
            var byteValue = _serializer.ToBinary(data);

            var l = Lmdb;
            tx.Put(l.db, byteKey, byteValue);
        }

        private void DoWriteBehind()
        {
            if (_pending.Count > 0)
            {
                var (env, _, _) = Lmdb;
                var t0 = Stopwatch.StartNew();
                using (var tx = env.BeginTransaction())
                {
                    try
                    {
                        foreach (var entry in _pending)
                        {
                            DbPut(tx, entry.Key, entry.Value);
                        }
                        tx.Commit();

                        t0.Stop();
                        if (_log.IsDebugEnabled)
                        {
                            _log.Debug($"store and commit of [{_pending.Count}] entries took {t0.ElapsedMilliseconds} ms");
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
