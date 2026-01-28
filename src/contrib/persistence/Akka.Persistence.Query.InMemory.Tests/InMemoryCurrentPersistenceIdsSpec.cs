//-----------------------------------------------------------------------
// <copyright file="InMemoryCurrentPersistenceIdsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Persistence.TCK.Query;
using Xunit.Abstractions;

namespace Akka.Persistence.Query.InMemory.Tests
{
    public class InMemoryCurrentPersistenceIdsSpec : CurrentPersistenceIdsSpec
    {
        private static Config Config() => ConfigurationFactory.ParseString("akka.loglevel = DEBUG")
            .WithFallback(InMemoryPersistenceSpecConfig.Config);

        public InMemoryCurrentPersistenceIdsSpec(ITestOutputHelper output) :
            base(Config(), nameof(InMemoryCurrentPersistenceIdsSpec), output)
        {
            InMemoryPersistenceSpecConfig.EnsureThreadPoolWarmed();

            // Force-load Persistence extension to trigger auto-start-journals/snapshot-stores
            // This ensures RecoveryPermitter is initialized before any persistent actors are created
            Persistence.Instance.Apply(Sys);
            ReadJournal = Sys.ReadJournalFor<InMemoryReadJournal>(InMemoryReadJournal.Identifier);
        }
    }
}
