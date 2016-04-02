using System;
using System.Collections.Generic;
using System.Reactive.Streams;
using Akka.Configuration;
using Akka.Streams.Dsl;

namespace Akka.Persistence.Query.Tests
{
    /// <summary>
    /// Use for tests only!
    /// Emits infinite stream of strings (representing queried for events).
    /// </summary>
    public class DummyReadJournal : IAllPersistenceIdsQuery
    {
        public static readonly string Identifier = "akka.persistence.query.journal.dummy";

        public Source<string, Unit> AllPersistenceIds => Source.From(Iterate(0)).Map(i => i.ToString());

        private IEnumerable<int> Iterate(int start)
        {
            while (true) yield return start++;
        }
    }

    public class DummyReadJournalProvider : IReadJournalProvider
    {
        public static Config Config => ConfigurationFactory.ParseString(
            $@"{DummyReadJournal.Identifier} {{ class = ""{typeof (DummyReadJournalProvider).FullName}, Akka.Persistence.Query.Tests"" }}");

        public IReadJournal GetReadJournal()
        {
            return new DummyReadJournal();
        }
    }
}