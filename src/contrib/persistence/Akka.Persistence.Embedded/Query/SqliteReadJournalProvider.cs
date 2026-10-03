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
        private readonly string _pluginPath;

        /// <summary>
        /// Called by Akka.Persistence.Query with the plugin's config section when HOCON names this class. The provider
        /// finds the section's path by looking for the section under <c>akka.persistence.query.journal</c> that
        /// <paramref name="config"/> came from.
        /// </summary>
        /// <exception cref="Akka.Configuration.ConfigurationException">No section there matches <paramref name="config"/>.
        /// Register the plugin with <c>WithEmbeddedPersistence</c> instead, or use the constructor that takes the plugin path.</exception>
        public SqliteReadJournalProvider(ExtendedActorSystem system, Config config)
            : this(system, config, FindPluginPath(system, config))
        {
        }

        /// <summary>Creates the provider for a read journal registered under <paramref name="pluginPath"/>.</summary>
        public SqliteReadJournalProvider(ExtendedActorSystem system, Config config, string pluginPath)
        {
            _system = system;
            _config = config;
            _pluginPath = pluginPath;
        }

        /// <inheritdoc />
        public Akka.Persistence.Query.IReadJournal GetReadJournal()
            => new SqliteReadJournal(_system, _config.WithFallback(SqlitePersistence.DefaultQueryConfiguration), _pluginPath);

        private static string FindPluginPath(ExtendedActorSystem system, Config config)
        {
            const string parentPath = "akka.persistence.query.journal";
            var parent = system.Settings.Config.GetConfig(parentPath);
            var matches = new System.Collections.Generic.List<string>();
            if (parent is not null)
            {
                var wanted = config.ToString();
                foreach (var entry in parent.AsEnumerable())
                {
                    var section = parent.GetConfig(entry.Key);
                    if (section is not null && string.Equals(section.ToString(), wanted, System.StringComparison.Ordinal))
                        matches.Add($"{parentPath}.{entry.Key}");
                }
            }

            // two sections with the same text cannot be told apart either
            if (matches.Count == 1)
                return matches[0];

            // Guessing the default id would read another plugin's settings and log under the wrong name, so refuse.
            throw new Akka.Configuration.ConfigurationException(
                $"[{typeof(SqliteReadJournalProvider).FullName}] could not tell which section of [{parentPath}] its config came from ({matches.Count} sections match). " +
                "Register the read journal with WithEmbeddedPersistence (Akka.Persistence.Embedded.Hosting), or create the provider with its plugin path.");
        }
    }
}
