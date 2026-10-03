//-----------------------------------------------------------------------
// <copyright file="PersistenceQuery.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Util;

namespace Akka.Persistence.Query
{
    public sealed class PersistenceQuery : IExtension
    {
        private static readonly Type ReadJournalType = typeof(IReadJournal);
        
        private readonly ExtendedActorSystem _system;
        private readonly PersistencePluginRegistry _registry;
        private readonly ConcurrentDictionary<string, IReadJournal> _readJournalPluginExtensionIds = new();
        private ILoggingAdapter _log;
        private readonly object _lock = new ();

        public static PersistenceQuery Get(ActorSystem system)
        {
            return system.WithExtension<PersistenceQuery, PersistenceQueryProvider>();
        }

        public ILoggingAdapter Log => _log ??= _system.Log;

        public PersistenceQuery(ExtendedActorSystem system)
        {
            _system = system;
            _registry = PersistencePluginRegistry.For(system);
        }

        public TJournal ReadJournalFor<TJournal>(string readJournalPluginId) where TJournal : IReadJournal
            => (TJournal) ReadJournalFor(typeof(TJournal), readJournalPluginId);

        public IReadJournal ReadJournalFor(Type readJournalType, string readJournalPluginId)
        {
            if(!ReadJournalType.IsAssignableFrom(readJournalType))
                throw new ArgumentException("Must implement IReadJournal interface", nameof(readJournalType));
            
            if(_readJournalPluginExtensionIds.TryGetValue(readJournalPluginId, out var plugin))
                return plugin;
            
            lock (_lock)
            {
                if (_readJournalPluginExtensionIds.TryGetValue(readJournalPluginId, out plugin))
                    return plugin;
                
                // a registered read journal brings its own default config; otherwise it is found by reflection on
                // the journal type, which a trimmed app cannot do
                Config defaultConfig = null;
                if (!_registry.TryGet<ReadJournalDetails>(readJournalPluginId, out _) && AkkaFeatures.IsDynamicTypeLoadingSupported)
                    defaultConfig = GetDefaultConfigByReflection(readJournalType);

                plugin = CreatePlugin(readJournalPluginId, defaultConfig).GetReadJournal();
                _readJournalPluginExtensionIds[readJournalPluginId] = plugin;
                return plugin;
            }
        }

        private IReadJournalProvider CreatePlugin(string configPath, Config config)
        {
            // lookup order, written out at the site on purpose (see AkkaFeatures): registration (by plugin id), built-in (none), guard, reflection
            if (_registry.TryGet<ReadJournalDetails>(configPath, out var registered))
            {
                // HOCON first, then the registration's default config. A registered read journal needs no `class` setting.
                var section = _system.Settings.Config.HasPath(configPath) ? _system.Settings.Config.GetConfig(configPath) : Config.Empty;
                if (registered.DefaultConfig is { } defaultConfig)
                    section = section.WithFallback(defaultConfig);

                return registered.CreateProvider(_system, section);
            }

            if (config != null)
                _system.Settings.InjectTopLevelFallback(config);

            if (string.IsNullOrEmpty(configPath) || !_system.Settings.Config.HasPath(configPath))
                throw new ArgumentException("HOCON config is missing persistence read journal plugin config path: " + configPath);

            var pluginConfig = _system.Settings.Config.GetConfig(configPath);
            var pluginTypeName = pluginConfig.GetString("class", null);

            if (!AkkaFeatures.IsDynamicTypeLoadingSupported)
                throw new ConfigurationException(AkkaFeatures.NotBuiltIn(
                    $"{configPath}.class",
                    pluginTypeName,
                    "a read journal registered through Akka.Persistence.Hosting (WithReadJournal)"));

            return CreatePluginByReflection(pluginTypeName, pluginConfig);
        }

        [RequiresUnreferencedCode("Loads a read journal provider type named in HOCON by name. The trimmer cannot tell which type that is, so it may have been trimmed away.")]
        private static Config GetDefaultConfigByReflection(Type readJournalType)
            => GetDefaultConfig(readJournalType);

        [RequiresUnreferencedCode("Loads a read journal provider type named in HOCON by name. The trimmer cannot tell which type that is, so it may have been trimmed away.")]
        private IReadJournalProvider CreatePluginByReflection(string pluginTypeName, Config pluginConfig)
        {
            var pluginType = Type.GetType(pluginTypeName, true);

            return CreateType(pluginType, new object[] { _system, pluginConfig });
        }

        [RequiresUnreferencedCode("Loads a read journal provider type named in HOCON by name. The trimmer cannot tell which type that is, so it may have been trimmed away.")]
        private IReadJournalProvider CreateType(Type pluginType, object[] parameters)
        {
            var ctor = pluginType.GetConstructor(new Type[] { typeof(ExtendedActorSystem), typeof(Config) });
            if (ctor != null) return (IReadJournalProvider)ctor.Invoke(parameters);

            ctor = pluginType.GetConstructor(new Type[] { typeof(ExtendedActorSystem) });
            if (ctor != null) return (IReadJournalProvider)ctor.Invoke(new[] { parameters[0] });

            ctor = pluginType.GetConstructor(new Type[0]);
            if (ctor != null) return (IReadJournalProvider)ctor.Invoke(Array.Empty<object>());

            throw new ArgumentException($"Unable to create read journal plugin instance type {pluginType}!");
        }

        public static Config GetDefaultConfig<TJournal>()
            => GetDefaultConfig(typeof(TJournal));

        public static Config GetDefaultConfig(Type journalType)
        {
            var defaultConfigMethod = journalType.GetMethod("DefaultConfiguration", BindingFlags.Public | BindingFlags.Static);
            return defaultConfigMethod?.Invoke(null, null) as Config;
        }
    }

    public class PersistenceQueryProvider : ExtensionIdProvider<PersistenceQuery>
    {
        public override PersistenceQuery CreateExtension(ExtendedActorSystem system)
        {
            return new PersistenceQuery(system);
        }
    }

    public static class PersistenceQueryExtensions
    {
        public static TJournal ReadJournalFor<TJournal>(this ActorSystem system, string readJournalPluginId)
            where TJournal : IReadJournal
        {
            return PersistenceQuery.Get(system).ReadJournalFor<TJournal>(readJournalPluginId);
        }
    }
}
