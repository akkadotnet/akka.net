//-----------------------------------------------------------------------
// <copyright file="InMemoryCurrentEventsByTagSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Persistence.TCK.Query;
using Xunit;

namespace Akka.Persistence.Query.InMemory.Tests
{
    public class InMemoryCurrentEventsByTagSpec : CurrentEventsByTagSpec
    {
        private static Config Config() => ConfigurationFactory.ParseString(
                $$"""
                akka.loglevel = INFO
                akka.persistence.journal.inmem {
                    event-adapters {
                      color-tagger  = "{{typeof(ColorFruitTagger).FullName}}, {{typeof(ColorFruitTagger).Assembly.GetName().Name}}"
                    }
                    event-adapter-bindings = {
                      "System.String" = color-tagger
                    }
                }
                """)
            .WithFallback(InMemoryPersistenceSpecConfig.Config);

        public InMemoryCurrentEventsByTagSpec(ITestOutputHelper output) : 
            base(Config(), nameof(InMemoryCurrentEventsByTagSpec), output)
        {
            InMemoryPersistenceSpecConfig.EnsureThreadPoolWarmed();
            Persistence.Instance.Get(Sys); // Initialize persistence immediately
            ReadJournal = Sys.ReadJournalFor<InMemoryReadJournal>(InMemoryReadJournal.Identifier);
        }

        protected override bool SupportsTagsInEventEnvelope => true;
    }
}
