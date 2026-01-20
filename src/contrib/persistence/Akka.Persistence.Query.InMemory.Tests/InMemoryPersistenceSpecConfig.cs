// -----------------------------------------------------------------------
//  <copyright file="InMemoryPersistenceSpecConfig.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading;
using Akka.Configuration;

namespace Akka.Persistence.Query.InMemory.Tests;

public static class InMemoryPersistenceSpecConfig
{
    static InMemoryPersistenceSpecConfig()
    {
        // Pre-warm the thread pool to avoid cold-start delays in CI environments.
        // Thread pool growth is throttled (~500ms-1s per new thread), which can cause
        // actor recovery to timeout when many actors are created simultaneously.
        // This is especially important for persistence tests that create multiple
        // persistent actors that all need RecoveryPermitter grants.
        ThreadPool.GetMinThreads(out var minWorker, out var minIo);
        var targetMin = Math.Max(minWorker, Environment.ProcessorCount * 2);
        ThreadPool.SetMinThreads(targetMin, minIo);
    }

    /// <summary>
    /// Sets the refresh interval to 1s and uses the in-memory journal and snapshot store.
    /// </summary>
    public static readonly Config Config = ConfigurationFactory.ParseString(
            """
            akka.loglevel = DEBUG
            akka.persistence.query.journal.inmem.refresh-interval = 1s
            akka.persistence.journal.plugin = "akka.persistence.journal.inmem"
            akka.persistence.journal.auto-start-journals = ["akka.persistence.journal.inmem"]
            akka.persistence.snapshot-store.plugin = "akka.persistence.snapshot-store.inmem"
            akka.persistence.snapshot-store.auto-start-snapshot-stores = ["akka.persistence.snapshot-store.inmem"]
            """)
        .WithFallback(InMemoryReadJournal.DefaultConfiguration());
}