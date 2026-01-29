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
    /// <summary>
    /// Pre-warms the thread pool to avoid cold-start delays in CI environments.
    /// Thread pool growth is throttled (~500ms per new thread), which can cause
    /// actor recovery to timeout when many actors are created simultaneously.
    /// This is especially important for persistence tests that create multiple
    /// persistent actors that all need RecoveryPermitter grants.
    /// </summary>
    /// <remarks>
    /// This method is safe to call multiple times - it uses <see cref="Math.Max(int, int)"/> to ensure
    /// the thread pool minimum is never decreased from a previously set value.
    /// </remarks>
    public static void EnsureThreadPoolWarmed(int minimumThreadCount = -1)
    {
        if(minimumThreadCount < 0)
            minimumThreadCount = Environment.ProcessorCount * 2;

        ThreadPool.GetMinThreads(out var minWorker, out var minIo);
        var targetMin = Math.Max(minWorker, minimumThreadCount);
        ThreadPool.SetMinThreads(targetMin, minIo);
    }
    
    /// <summary>
    /// Sets the refresh interval to 1s and uses the in-memory journal and snapshot store.
    /// </summary>
    public static readonly Config Config = ConfigurationFactory.ParseString(
            """
            akka.persistence.query.journal.inmem.refresh-interval = 1s
            akka.persistence.journal.plugin = "akka.persistence.journal.inmem"
            akka.persistence.snapshot-store.plugin = "akka.persistence.snapshot-store.inmem"
            """)
        .WithFallback(InMemoryReadJournal.DefaultConfiguration());
}