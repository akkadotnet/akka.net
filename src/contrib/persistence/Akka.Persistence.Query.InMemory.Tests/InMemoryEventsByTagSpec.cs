//-----------------------------------------------------------------------
// <copyright file="InMemoryEventsByTagSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Persistence.TCK.Query;
using Xunit.Abstractions;

namespace Akka.Persistence.Query.InMemory.Tests
{
    public class InMemoryEventsByTagSpec : EventsByTagSpec
    {
        private static Config Config() => ConfigurationFactory.ParseString(@"
            akka.loglevel = INFO
            akka.persistence.journal.plugin = ""akka.persistence.journal.inmem""
            akka.persistence.snapshot-store.plugin = ""akka.persistence.snapshot-store.inmem""
            akka.persistence.journal.inmem {
                event-adapters {
                  color-tagger  = ""Akka.Persistence.TCK.Query.ColorFruitTagger, Akka.Persistence.TCK""
                }
                event-adapter-bindings = {
                  ""System.String"" = color-tagger
                }
            }")
            .WithFallback(InMemoryReadJournal.DefaultConfiguration());

        public InMemoryEventsByTagSpec(ITestOutputHelper output) : 
            base(Config(), nameof(InMemoryCurrentPersistenceIdsSpec), output)
        {
            ReadJournal = Sys.ReadJournalFor<InMemoryReadJournal>(InMemoryReadJournal.Identifier);
        }

        protected override bool SupportsTagsInEventEnvelope => true;
    }
}
