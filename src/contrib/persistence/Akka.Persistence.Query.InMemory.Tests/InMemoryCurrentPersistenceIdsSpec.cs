﻿// -----------------------------------------------------------------------
//  <copyright file="InMemoryCurrentPersistenceIdsSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Persistence.TCK.Query;
using Xunit.Abstractions;

namespace Akka.Persistence.Query.InMemory.Tests;

public class InMemoryCurrentPersistenceIdsSpec : CurrentPersistenceIdsSpec
{
    public InMemoryCurrentPersistenceIdsSpec(ITestOutputHelper output) :
        base(Config(), nameof(InMemoryCurrentPersistenceIdsSpec), output)
    {
        ReadJournal = Sys.ReadJournalFor<InMemoryReadJournal>(InMemoryReadJournal.Identifier);
    }

    private static Config Config()
    {
        return ConfigurationFactory.ParseString(@"
            akka.loglevel = INFO
            akka.persistence.journal.plugin = ""akka.persistence.journal.inmem""
            akka.persistence.snapshot-store.plugin = ""akka.persistence.snapshot-store.inmem""")
            .WithFallback(InMemoryReadJournal.DefaultConfiguration());
    }
}