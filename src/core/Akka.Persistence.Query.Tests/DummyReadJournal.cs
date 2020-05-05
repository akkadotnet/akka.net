//-----------------------------------------------------------------------
// <copyright file="DummyReadJournal.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using Akka.Configuration;
using Akka.Streams.Dsl;

namespace Akka.Persistence.Query.Tests
{
    /// <summary>
    /// Use for tests only!
    /// Emits infinite stream of strings (representing queried for events).
    /// </summary>
    public class DummyReadJournal : IPersistenceIdsQuery
    {
        public static readonly string Identifier = "akka.persistence.query.journal.dummy";

        public Source<string, NotUsed> PersistenceIds() => Source.From(Iterate(0)).Select(i => i.ToString());

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
