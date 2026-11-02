//-----------------------------------------------------------------------
// <copyright file="InMemoryCurrentAllEventsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Persistence.TCK.Query;
using Xunit;

namespace Akka.Persistence.Query.InMemory.Tests
{
    public class InMemoryCurrentAllEventsSpec : CurrentAllEventsSpec
    {
        private static Config Config() => ConfigurationFactory.ParseString("akka.loglevel = INFO")
            .WithFallback(InMemoryPersistenceSpecConfig.Config);

        public InMemoryCurrentAllEventsSpec(ITestOutputHelper output) : base(Config(), nameof(InMemoryCurrentAllEventsSpec), output)
        {
            ReadJournal = Sys.ReadJournalFor<InMemoryReadJournal>(InMemoryReadJournal.Identifier);
        }
    }
}
