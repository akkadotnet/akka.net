﻿// -----------------------------------------------------------------------
//  <copyright file="InMemoryCurrentAllEventsSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Persistence.TCK.Query;
using Xunit.Abstractions;

namespace Akka.Persistence.Query.InMemory.Tests;

public class InMemoryCurrentAllEventsSpec : CurrentAllEventsSpec
{
    public InMemoryCurrentAllEventsSpec(ITestOutputHelper output) : base(Config(), nameof(InMemoryCurrentAllEventsSpec),
        output)
    {
        ReadJournal = Sys.ReadJournalFor<InMemoryReadJournal>(InMemoryReadJournal.Identifier);
    }

    private static Config Config()
    {
        return ConfigurationFactory.ParseString(@"
            akka.loglevel = INFO
            akka.persistence.query.journal.inmem.refresh-interval = 1s
            akka.persistence.journal.plugin = ""akka.persistence.journal.inmem""
            akka.persistence.snapshot-store.plugin = ""akka.persistence.snapshot-store.inmem""")
            .WithFallback(InMemoryReadJournal.DefaultConfiguration());
    }
}