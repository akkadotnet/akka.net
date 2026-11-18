// -----------------------------------------------------------------------
//  <copyright file="TypedPluginOptions.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Diagnostics.CodeAnalysis;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Query;

#nullable enable
namespace Akka.Persistence.Hosting
{
    /// <summary>
    /// Base class for a journal options class that names its journal actor type in code. <c>WithJournal</c> then
    /// starts the journal from that type, so the plugin also starts with <c>Akka.DynamicTypeLoading</c> off
    /// (Native AOT, trimmed apps). The HOCON <c>class</c> setting still decides on the JIT.
    /// </summary>
    /// <typeparam name="TJournal">The journal actor. It needs a public constructor that takes the plugin's
    /// <see cref="Config"/>, or a public parameterless one, the same rule HOCON <c>class</c> follows.</typeparam>
    public abstract class JournalOptions<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.Interfaces)] TJournal>
        : JournalOptions where TJournal : ActorBase
    {
        protected JournalOptions(bool isDefault) : base(isDefault)
        {
        }

        internal sealed override JournalDetails CreateJournalDetails()
            => JournalDetails.Create(PluginId, PluginActivator.ActorFactory<TJournal>());
    }

    /// <summary>
    /// Base class for a journal options class that names its journal actor type and its default read journal in
    /// code. <c>WithJournal</c> starts both from those types, so they also start with <c>Akka.DynamicTypeLoading</c> off.
    /// </summary>
    /// <typeparam name="TJournal">The journal actor, see <see cref="JournalOptions{TJournal}"/>.</typeparam>
    /// <typeparam name="TReadJournalProvider">The plugin's read journal provider. It needs a public constructor
    /// that takes <c>(ExtendedActorSystem, Config)</c>, <c>(ExtendedActorSystem)</c> or nothing, the same rule
    /// HOCON <c>class</c> follows.</typeparam>
    public abstract class JournalOptions<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.Interfaces)] TJournal,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TReadJournalProvider>
        : JournalOptions<TJournal> where TJournal : ActorBase where TReadJournalProvider : class, IReadJournalProvider
    {
        protected JournalOptions(bool isDefault) : base(isDefault)
        {
        }

        /// <summary>
        /// The config path of this journal's read journal, which <c>ReadJournalFor</c> is called with.
        /// <b>Default</b>: <c>akka.persistence.query.journal.{Identifier}</c>. Override it when the plugin uses another path.
        /// </summary>
        protected virtual string ReadJournalPluginId => $"akka.persistence.query.journal.{Identifier}";

        internal sealed override ReadJournalDetails CreateReadJournalDetails()
            => ReadJournalDetails.Create(ReadJournalPluginId, PluginActivator.ReadJournalFactory<TReadJournalProvider>());
    }

    /// <summary>
    /// Base class for a snapshot store options class that names its snapshot store actor type in code.
    /// <c>WithSnapshot</c> then starts the store from that type, so the plugin also starts with
    /// <c>Akka.DynamicTypeLoading</c> off. The HOCON <c>class</c> setting still decides on the JIT.
    /// </summary>
    /// <typeparam name="TSnapshotStore">The snapshot store actor. It needs a public constructor that takes the
    /// plugin's <see cref="Config"/>, or a public parameterless one.</typeparam>
    public abstract class SnapshotOptions<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.Interfaces)] TSnapshotStore>
        : SnapshotOptions where TSnapshotStore : ActorBase
    {
        protected SnapshotOptions(bool isDefault) : base(isDefault)
        {
        }

        internal sealed override SnapshotStoreDetails CreateSnapshotStoreDetails()
            => SnapshotStoreDetails.Create(PluginId, PluginActivator.ActorFactory<TSnapshotStore>());
    }

    /// <summary>
    /// INTERNAL API
    ///
    /// Creates plugins from a type named in code, with the constructor rules core applies to a HOCON <c>class</c>.
    /// The type parameters carry the trimming annotations, so this is safe under Native AOT.
    /// </summary>
    internal static class PluginActivator
    {
        private static readonly Type[] ConfigArg = { typeof(Config) };
        private static readonly Type[] SystemAndConfigArgs = { typeof(ExtendedActorSystem), typeof(Config) };
        private static readonly Type[] SystemArg = { typeof(ExtendedActorSystem) };

        /// <summary>
        /// The <c>(Config)</c> constructor where the type has one, else the parameterless one. Called inside the
        /// actor's creation context, so a failure surfaces where a HOCON <c>class</c> failure would.
        /// </summary>
        public static Func<Config, T> ActorFactory<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
            where T : ActorBase
        {
            var withConfig = typeof(T).GetConstructor(ConfigArg);
            if (withConfig is not null)
                return config => (T)withConfig.Invoke(new object[] { config });

            return _ => (T)(Activator.CreateInstance(typeof(T))
                            ?? throw new ArgumentException($"Unable to create persistence plugin instance type {typeof(T)}!"));
        }

        /// <summary>
        /// The same constructor search <c>PersistenceQuery</c> runs on a HOCON <c>class</c>.
        /// </summary>
        public static Func<ExtendedActorSystem, Config, T> ReadJournalFactory<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
            where T : class, IReadJournalProvider
        {
            var type = typeof(T);
            if (type.GetConstructor(SystemAndConfigArgs) is { } full)
                return (system, config) => (T)full.Invoke(new object[] { system, config });
            if (type.GetConstructor(SystemArg) is { } systemOnly)
                return (system, _) => (T)systemOnly.Invoke(new object[] { system });
            if (type.GetConstructor(Type.EmptyTypes) is { } parameterless)
                return (_, _) => (T)parameterless.Invoke(Array.Empty<object>());

            // thrown on use, as the HOCON path would, so registering never fails on the JIT where HOCON decides
            return (_, _) => throw new ArgumentException($"Unable to create read journal plugin instance type {type}!");
        }
    }
}
