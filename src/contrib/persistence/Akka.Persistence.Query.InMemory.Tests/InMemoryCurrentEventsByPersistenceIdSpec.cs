//-----------------------------------------------------------------------
// <copyright file="InMemoryCurrentEventsByPersistenceIdSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Persistence.TCK.Query;
using Xunit;

namespace Akka.Persistence.Query.InMemory.Tests
{
    public class InMemoryCurrentEventsByPersistenceIdSpec : CurrentEventsByPersistenceIdSpec
    {
        private static Config Config() => ConfigurationFactory.ParseString("akka.loglevel = INFO")
            .WithFallback(InMemoryPersistenceSpecConfig.Config);

        public InMemoryCurrentEventsByPersistenceIdSpec(ITestOutputHelper output) : 
            base(Config(), nameof(InMemoryCurrentEventsByPersistenceIdSpec), output)
        {
            InMemoryPersistenceSpecConfig.EnsureThreadPoolWarmed();
            Persistence.Instance.Get(Sys); // Initialize persistence immediately
            ReadJournal = Sys.ReadJournalFor<InMemoryReadJournal>(InMemoryReadJournal.Identifier);
        }
    }
}
