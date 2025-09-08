//-----------------------------------------------------------------------
// <copyright file="InMemoryEventsByTagSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Persistence.TCK.Query;
using Akka.Tests.Shared.Internals;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Query.InMemory.Tests
{
    public class InMemoryEventsByTagSpec : EventsByTagSpec
    {
        private static Config Config() => ConfigurationFactory.ParseString(@"
            akka.loglevel = INFO
            akka.persistence.journal.inmem {
                event-adapters {
                  color-tagger  = ""Akka.Persistence.TCK.Query.ColorFruitTagger, Akka.Persistence.TCK""
                }
                event-adapter-bindings = {
                  ""System.String"" = color-tagger
                }
            }")
            .WithFallback(InMemoryPersistenceSpecConfig.Config);

        public InMemoryEventsByTagSpec(ITestOutputHelper output) : 
            base(Config(), nameof(InMemoryCurrentPersistenceIdsSpec), output)
        {
            ReadJournal = Sys.ReadJournalFor<InMemoryReadJournal>(InMemoryReadJournal.Identifier);
        }

        protected override bool SupportsTagsInEventEnvelope => true;

        public override void ReadJournal_live_query_EventsByTag_should_find_events_from_offset_exclusive()
        {
            // disabled for testing purposes
        }

        [Theory]
        [Repeat(1000)]
        public void ReadJournal_live_query_EventsByTag_should_find_events_from_offset_exclusive_repeated(int _)
        {
            base.ReadJournal_live_query_EventsByTag_should_find_events_from_offset_exclusive();
        }
    }
}
