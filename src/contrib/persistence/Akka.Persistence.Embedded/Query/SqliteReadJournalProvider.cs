//-----------------------------------------------------------------------
// <copyright file="SqliteReadJournalProvider.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using Akka.Actor;
using Akka.Configuration;

namespace Akka.Persistence.Embedded.Query
{
    /// <summary>Creates the <see cref="SqliteReadJournal"/>. Named by the <c>class</c> setting of the read journal plugin.</summary>
    public sealed class SqliteReadJournalProvider : Akka.Persistence.Query.IReadJournalProvider
    {
        private readonly ExtendedActorSystem _system;
        private readonly Config _config;

        /// <summary>Called by Akka.Persistence.Query with the plugin's config section.</summary>
        public SqliteReadJournalProvider(ExtendedActorSystem system, Config config)
        {
            _system = system;
            _config = config;
        }

        /// <inheritdoc />
        public Akka.Persistence.Query.IReadJournal GetReadJournal()
            => new SqliteReadJournal(_system, _config.WithFallback(SqlitePersistence.DefaultQueryConfiguration));
    }
}
