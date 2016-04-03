//-----------------------------------------------------------------------
// <copyright file="SqliteReadJournalProvider.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
using Akka.Actor;
using Akka.Configuration;

namespace Akka.Persistence.Query.Sqlite
{
    public class SqliteReadJournalProvider : IReadJournalProvider
    {
        private readonly ExtendedActorSystem _system;
        private readonly Config _config;

        public SqliteReadJournalProvider(ExtendedActorSystem system, Config config)
        {
            _system = system;
            _config = config;
        }

        public IReadJournal GetReadJournal()
        {
            return new SqliteReadJournal(_system, _config);
        }
    }
}