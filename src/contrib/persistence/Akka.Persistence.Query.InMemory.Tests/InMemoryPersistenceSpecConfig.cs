// -----------------------------------------------------------------------
//  <copyright file="InMemoryPersistenceSpecConfig.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using Akka.Configuration;

namespace Akka.Persistence.Query.InMemory.Tests;

public static class InMemoryPersistenceSpecConfig
{
    /// <summary>
    /// Sets the refresh interval to 1s and uses the in-memory journal and snapshot store.
    /// </summary>
    public static readonly Config Config = ConfigurationFactory.ParseString("""
                                                                            
                                                                                        akka.persistence.query.journal.inmem.refresh-interval = 1s
                                                                                        akka.persistence.journal.plugin = "akka.persistence.journal.inmem"
                                                                                        akka.persistence.snapshot-store.plugin = "akka.persistence.snapshot-store.inmem"
                                                                            """)
        .WithFallback(InMemoryReadJournal.DefaultConfiguration());
}