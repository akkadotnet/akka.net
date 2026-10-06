//-----------------------------------------------------------------------
// <copyright file="EventAdapters.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using Akka.Actor;
using Akka.Configuration;
using Akka.Configuration.Hocon;
using Akka.Event;
using Akka.Pattern;
using Akka.Util;

namespace Akka.Persistence.Journal
{
    /// <summary>
    /// <para>An <see cref="IEventAdapter"/> is both a <see cref="IWriteEventAdapter"/> and a <see cref="IReadEventAdapter"/>.
    /// Facility to convert from and to specialised data models, as may be required by specialized persistence Journals.</para>
    ///
    /// <para>Typical use cases include (but are not limited to):</para>
    /// <para>- adding metadata, a.k.a. "tagging" - by wrapping objects into tagged counterparts</para>
    /// <para>- manually converting to the Journals storage format, such as JSON, BSON or any specialised binary format</para>
    /// <para>- adapting incoming events in any way before persisting them by the journal</para>
    /// </summary>
    public interface IEventAdapter : IWriteEventAdapter, IReadEventAdapter
    {
    }

    /// <summary>
    /// <para>Facility to convert to specialised data models, as may be required by specialized persistence Journals.</para>
    ///
    /// <para>Typical use cases include (but are not limited to):</para>
    /// <para>- adding metadata, a.k.a. "tagging" - by wrapping objects into tagged counterparts</para>
    /// <para>- manually converting to the Journals storage format, such as JSON, BSON or any specialised binary format</para>
    /// <para>- splitting up large events into sequences of smaller ones</para>
    /// </summary>
    public interface IWriteEventAdapter
    {
        /// <summary>
        /// Return the manifest (type hint) that will be provided in the <see cref="IReadEventAdapter.FromJournal"/> method.
        /// Use empty string if not needed.
        /// </summary>
        /// <param name="evt">Application event for which to provide a manifest.</param>
        /// <returns>Manifest (type hint) to pass to <see cref="IReadEventAdapter.FromJournal"/>, or an empty string when no manifest is needed.</returns>
        string Manifest(object evt);

        /// <summary>
        /// <para>Convert domain event to journal event type.</para>
        ///
        /// <para>Some journal may require a specific type to be returned to them,
        /// for example if a primary key has to be associated with each event then a journal
        /// may require adapters to return "EventWithPrimaryKey(event, key)".</para>
        ///
        /// <para>The <see cref="ToJournal"/> adaptation must be an 1-to-1 transformation.
        /// It is not allowed to drop incoming events during the `toJournal` adaptation.</para>
        /// </summary>
        /// <param name="evt">the application-side domain event to be adapted to the journal model</param>
        /// <returns>the adapted event object, possibly the same object if no adaptation was performed</returns>
        object ToJournal(object evt);
    }

    /// <summary>
    /// <para>Facility to convert from specialised data models, as may be required by specialized persistence Journals.</para>
    ///
    /// <para>Typical use cases include (but are not limited to):</para>
    /// <para>- extracting events from "envelopes"</para>
    /// <para>- manually converting to the Journals storage format, such as JSON, BSON or any specialised binary format</para>
    /// <para>- adapting incoming events from a "data model" to the "domain model"</para>
    /// </summary>
    public interface IReadEventAdapter
    {
        /// <summary>
        /// <para>Convert an event from its journal model to the application's domain model.</para>
        ///
        /// <para>One event may be adapter into multiple(or none) events which should be delivered to the <see cref="PersistentActor"/>.
        /// Use the specialised <see cref="EventSequence.Single"/> method to emit exactly one event,
        /// or <see cref="EventSequence.Empty"/> in case the adapter is not handling this event. Multiple <see cref="IEventAdapter"/> instances are
        /// applied in order as defined in configuration and their emitted event seqs are concatenated and delivered in order
        /// to the PersistentActor.</para>
        /// </summary>
        /// <param name="evt">event to be adapted before delivering to the PersistentActor</param>
        /// <param name="manifest">optionally provided manifest(type hint) in case the Adapter has stored one for this event. Use empty string if none.</param>
        /// <returns>sequence containing the adapted events (possibly zero) which will be delivered to the PersistentActor</returns>
        IEventSequence FromJournal(object evt, string manifest);
    }

    /// <summary>
    /// No-op model adapter which passes through the incoming events as-is.
    /// </summary>
    [Serializable]
    public sealed class IdentityEventAdapter : IEventAdapter
    {
        /// <summary>
        /// The singleton instance of <see cref="IdentityEventAdapter"/>.
        /// </summary>
        public static IdentityEventAdapter Instance { get; } = new();

        private IdentityEventAdapter() { }

        /// <inheritdoc/>
        public string Manifest(object evt)
        {
            return string.Empty;
        }

        /// <inheritdoc/>
        public object ToJournal(object evt)
        {
            return evt;
        }

        /// <inheritdoc/>
        public IEventSequence FromJournal(object evt, string manifest)
        {
            return EventSequence.Single(evt);
        }
    }

    [Serializable]
    internal class NoopWriteEventAdapter : IEventAdapter
    {
        private readonly IReadEventAdapter _readEventAdapter;

        public NoopWriteEventAdapter(IReadEventAdapter readEventAdapter)
        {
            _readEventAdapter = readEventAdapter;
        }

        public string Manifest(object evt)
        {
            return string.Empty;
        }

        public object ToJournal(object evt)
        {
            return evt;
        }

        public IEventSequence FromJournal(object evt, string manifest)
        {
            return _readEventAdapter.FromJournal(evt, manifest);
        }
    }

    [Serializable]
    internal class NoopReadEventAdapter : IEventAdapter
    {
        private readonly IWriteEventAdapter _writeEventAdapter;

        public NoopReadEventAdapter(IWriteEventAdapter writeEventAdapter)
        {
            _writeEventAdapter = writeEventAdapter;
        }

        public string Manifest(object evt)
        {
            return _writeEventAdapter.Manifest(evt);
        }

        public object ToJournal(object evt)
        {
            return _writeEventAdapter.ToJournal(evt);
        }

        public IEventSequence FromJournal(object evt, string manifest)
        {
            return EventSequence.Single(evt);
        }
    }

    /// <summary>
    /// Combines multiple read adapters by concatenating the event sequences they produce.
    /// </summary>
    [Serializable]
    public sealed class CombinedReadEventAdapter : IEventAdapter
    {
        private static readonly Exception OnlyReadSideException = new IllegalStateException(
                "CombinedReadEventAdapter must not be used when writing (creating manifests) events!");

        /// <summary>
        /// Read adapters applied in order when converting journal events.
        /// </summary>
        public IEnumerable<IEventAdapter> Adapters { get; }

        /// <summary>
        /// Initializes a combined read adapter.
        /// </summary>
        /// <param name="adapters">Adapters to apply in order.</param>
        public CombinedReadEventAdapter(IEnumerable<IEventAdapter> adapters)
        {
            Adapters = adapters.ToArray();
        }

        /// <summary>
        /// Writing is unsupported by a read-only combined adapter.
        /// </summary>
        /// <param name="evt">Event for which a manifest was requested.</param>
        /// <exception cref="IllegalStateException">Thrown because a combined read adapter cannot write events.</exception>
        /// <returns>This method throws <see cref="IllegalStateException"/>.</returns>
        public string Manifest(object evt)
        {
            throw OnlyReadSideException;
        }

        /// <summary>
        /// Writing is unsupported by a read-only combined adapter.
        /// </summary>
        /// <param name="evt">Event to adapt for journal storage.</param>
        /// <exception cref="IllegalStateException">Thrown because a combined read adapter cannot write events.</exception>
        /// <returns>This method throws <see cref="IllegalStateException"/>.</returns>
        public object ToJournal(object evt)
        {
            throw OnlyReadSideException;
        }

        /// <summary>
        /// Applies each read adapter to a journal event and concatenates the resulting event sequences.
        /// </summary>
        /// <param name="evt">Journal event to adapt.</param>
        /// <param name="manifest">Manifest associated with the journal event, or an empty string when none was stored.</param>
        /// <returns>The concatenated sequence of events produced by the adapters.</returns>
        public IEventSequence FromJournal(object evt, string manifest)
        {
            return EventSequence.Create(Adapters.SelectMany(adapter => adapter.FromJournal(evt, manifest).Events));
        }
    }

    [Serializable]
    internal class ReadWriteEventAdapter : IEventAdapter
    {
        private readonly IWriteEventAdapter _writeEventAdapter;
        private readonly IReadEventAdapter _readEventAdapter;

        public ReadWriteEventAdapter(IReadEventAdapter readEventAdapter, IWriteEventAdapter writeEventAdapter)
        {
            _readEventAdapter = readEventAdapter;
            _writeEventAdapter = writeEventAdapter;
        }

        public string Manifest(object evt)
        {
            return _writeEventAdapter.Manifest(evt);
        }

        public object ToJournal(object evt)
        {
            return _writeEventAdapter.ToJournal(evt);
        }

        public IEventSequence FromJournal(object evt, string manifest)
        {
            return _readEventAdapter.FromJournal(evt, manifest);
        }
    }

    /// <summary>
    /// Event adapter collection that returns the identity adapter for every event type.
    /// </summary>
    internal class IdentityEventAdapters : EventAdapters
    {
        /// <summary>
        /// Singleton event adapter collection that uses identity adaptation.
        /// </summary>
        public static readonly EventAdapters Instance = new IdentityEventAdapters();

        private IdentityEventAdapters() : base(null, null, null)
        {
        }

        /// <summary>
        /// Returns the identity adapter for every event type.
        /// </summary>
        /// <param name="type">Event type to adapt.</param>
        /// <returns>The singleton identity adapter.</returns>
        public override IEventAdapter Get(Type type)
        {
            return IdentityEventAdapter.Instance;
        }
    }

    /// <summary>
    /// Resolves event adapters for event types using the configured adapter bindings.
    /// </summary>
    public class EventAdapters
    {
        private readonly ConcurrentDictionary<Type, IEventAdapter> _map;
        private readonly IEnumerable<KeyValuePair<Type, IEventAdapter>> _bindings;
        private readonly ILoggingAdapter _log;

        /// <summary>
        /// Initializes a new instance of the <see cref="EventAdapters"/> class.
        /// </summary>
        /// <param name="system">Actor system used to instantiate configured adapters.</param>
        /// <param name="config">Configuration containing event adapter definitions and bindings.</param>
        /// <returns>Event adapters configured for the actor system.</returns>
        public static EventAdapters Create(ExtendedActorSystem system, Config config)
            => Create(system, config, string.Empty, null);

        /// <summary>
        /// INTERNAL API
        ///
        /// Same as <see cref="Create(ExtendedActorSystem, Config)"/>, but the caller names the plugin section
        /// that <paramref name="config"/> came from so that a failure points at the setting the user has to fix,
        /// and passes the adapters its journal registered in code. An empty path means the section is unknown,
        /// and the setting names start at <c>event-adapters</c>.
        /// <para>
        /// HOCON adapters and registered ones both apply. On a name clash the registered adapter wins when reflection is
        /// off, and HOCON wins when it is on, so that nothing changes for a running JIT app. A registered binding for an
        /// event type replaces a HOCON binding for the same type.
        /// </para>
        /// </summary>
        internal static EventAdapters Create(ExtendedActorSystem system, Config config, string pluginPath, IReadOnlyCollection<EventAdapterDetails>? registered)
        {
            var adapters = ConfigToMap(config, "event-adapters");
            var adapterBindings = ConfigToListMap(config, "event-adapter-bindings");
            registered ??= Array.Empty<EventAdapterDetails>();

            // With reflection on, an adapter that HOCON also names is built from HOCON as it always was, and a registration
            // only adds the adapters HOCON does not name. With reflection off the registrations stand in for HOCON.
            if (AkkaFeatures.IsDynamicTypeLoadingSupported)
                registered = registered.Where(r => !adapters.ContainsKey(r.Name)).ToList();

            var adapterNames = new HashSet<string>(adapters.Keys);
            adapterNames.UnionWith(registered.Select(r => r.Name));
            foreach (var kv in adapterBindings)
            {
                foreach (var boundAdapter in kv.Value)
                {
                    if (!adapterNames.Contains(boundAdapter))
                        throw new ArgumentException(string.Format("{0} was bound to undefined event-adapter: {1} (bindings: [{2}], known adapters: [{3}])",
                            kv.Key, boundAdapter, string.Join(", ", kv.Value), string.Join(", ", adapterNames)));
                }
            }

            // A Map of handler from alias to implementation (i.e. class implementing Akka.Serialization.ISerializer)
            // For example this defines a handler named 'country': `"country" -> com.example.comain.CountryTagsAdapter`
            var registeredNames = new HashSet<string>(registered.Select(r => r.Name));
            var handlers = adapters
                .Where(kv => !registeredNames.Contains(kv.Key))
                .ToDictionary(kv => kv.Key, kv => InstantiateAdapter(kv.Key, kv.Value, system, pluginPath));
            foreach (var details in registered)
                handlers[details.Name] = details.CreateAdapter(system);

            // The event types registered adapters are bound to. Two adapters on one type combine, as in HOCON.
            var registeredBindings = new Dictionary<Type, List<IEventAdapter>>();
            foreach (var details in registered)
            {
                foreach (var type in details.BoundTypes)
                {
                    if (!registeredBindings.TryGetValue(type, out var list))
                        registeredBindings[type] = list = new List<IEventAdapter>();
                    list.Add(handlers[details.Name]);
                }
            }

            // bindings is a enumerable of key-val representing the mapping from Type to handler.
            // It is primarily ordered by the most specific classes first, and secondly in the configured order.
            var pairs = adapterBindings.Select(kv =>
            {
                // lookup order, written out at the site on purpose (see AkkaFeatures): guard, reflection.
                // A registered binding never reaches here, it comes from an EventAdapterDetails.
                if (!AkkaFeatures.IsDynamicTypeLoadingSupported)
                {
                    // The binding Akka.Persistence.Hosting writes next to its registrations: every adapter it names is
                    // registered, and a registered adapter is bound to the type it names, so there is nothing to resolve.
                    // A hand-written binding of any other type is not in the registrations, so it fails with the rest.
                    if (kv.Value.All(registeredNames.Contains) && IsBoundByRegistration(kv.Key, registered))
                        return (KeyValuePair<Type, IEventAdapter>?)null;

                    throw new ConfigurationException(AkkaFeatures.NotBuiltIn(
                        $"{SettingPrefix(pluginPath)}event-adapter-bindings",
                        kv.Key,
                        "an event type bound through Akka.Persistence.Hosting (AddEventAdapter on the journal builder)"));
                }

                var type = ResolveBindingTypeByReflection(kv.Key);

                var adapter = kv.Value.Length == 1
                    ? handlers[kv.Value[0]]
                    : CombineAdapters(kv.Value.Select(h => handlers[h]));
                return (KeyValuePair<Type, IEventAdapter>?)new KeyValuePair<Type, IEventAdapter>(type, adapter);
            }).Where(pair => pair.HasValue && !registeredBindings.ContainsKey(pair.Value.Key)).Select(pair => pair!.Value).ToList();

            pairs.AddRange(registeredBindings.Select(kv => new KeyValuePair<Type, IEventAdapter>(
                kv.Key, kv.Value.Count == 1 ? kv.Value[0] : CombineAdapters(kv.Value))));

            var bindings = Sort(pairs);

            var backing = new ConcurrentDictionary<Type, IEventAdapter>();

            foreach (var pair in bindings)
            {
                backing.AddOrUpdate(pair.Key, pair.Value, (_, _) => pair.Value);
            }

            return new EventAdapters(backing, bindings, system.Log);
        }

        private static List<KeyValuePair<Type, IEventAdapter>> Sort(List<KeyValuePair<Type, IEventAdapter>> bindings)
        {
            return bindings.Aggregate(new List<KeyValuePair<Type, IEventAdapter>>(bindings.Count), (buf, ca) =>
            {

                var idx = IndexWhere(buf, x => x.Key.IsAssignableFrom(ca.Key));

                if (idx == -1)
                    buf.Add(ca);
                else
                    buf.Insert(idx, ca);

                return buf;
            });
        }

        private static IEventAdapter CombineAdapters(IEnumerable<IEventAdapter> adapters)
        {
            var writeAdapters = adapters.Where(a => a is NoopReadEventAdapter);
            if (writeAdapters.Count() == 0)
                return new NoopWriteEventAdapter(new CombinedReadEventAdapter(adapters));
            else if (writeAdapters.Count() == 1)
                return new ReadWriteEventAdapter(new CombinedReadEventAdapter(adapters.Where(a => a is NoopWriteEventAdapter)), writeAdapters.First());
            throw new IllegalStateException("Cannot have multiple write adapters for a single adapter binding");
        }

        private static int IndexWhere<T>(IList<T> list, Predicate<T> predicate)
        {
            for (int i = 0; i < list.Count; i++)
                if (predicate(list[i])) return i;

            return -1;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="EventAdapters"/> class.
        /// </summary>
        /// <param name="map">Cache of adapters resolved for event types.</param>
        /// <param name="bindings">Configured event type bindings ordered from most specific to least specific.</param>
        /// <param name="log">Logger used by adapter resolution.</param>
        protected EventAdapters(ConcurrentDictionary<Type, IEventAdapter> map, IEnumerable<KeyValuePair<Type, IEventAdapter>> bindings, ILoggingAdapter log)
        {
            _map = map;
            _bindings = bindings;
            _log = log;
        }

        /// <summary>
        /// Gets the event adapter configured for type <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">Event type to adapt.</typeparam>
        /// <returns>The adapter configured for <typeparamref name="T"/>, or the identity adapter when no binding matches.</returns>
        public IEventAdapter Get<T>()
        {
            return Get(typeof(T));
        }

        /// <summary>
        /// Gets the most specific configured event adapter for an event type.
        /// </summary>
        /// <param name="type">Event type to adapt.</param>
        /// <returns>The most specific matching adapter, or the identity adapter when no binding matches.</returns>
        public virtual IEventAdapter Get(Type type)
        {
            if (_map.TryGetValue(type, out IEventAdapter adapter))
                return adapter;

            // bindings are ordered from most specific to least specific
            var pair = _bindings.FirstOrDefault(kv => kv.Key.IsAssignableFrom(type));
            var value = !pair.Equals(default(KeyValuePair<Type, IEventAdapter>)) ? pair.Value : IdentityEventAdapter.Instance;

            adapter = _map.GetOrAdd(type, value);
            return adapter;
        }

        // a name comparison, nothing is loaded: the binding key's type name against the full name of each registered bound type
        private static bool IsBoundByRegistration(string bindingKey, IReadOnlyCollection<EventAdapterDetails> registered)
            => Akka.Util.TypeExtensions.TrySplitTypeName(bindingKey, out var name, out _)
               && registered.Any(r => r.BoundTypes.Any(t => string.Equals(t.FullName, name, StringComparison.Ordinal)));

        private static string SettingPrefix(string pluginPath)
            => string.IsNullOrEmpty(pluginPath) ? string.Empty : pluginPath + ".";

        private static IEventAdapter InstantiateAdapter(string adapterName, string qualifiedName, ExtendedActorSystem system, string pluginPath)
        {
            // lookup order, written out at the site on purpose (see AkkaFeatures): guard, reflection.
            // A registered adapter never reaches here, it comes from an EventAdapterDetails.
            if (!AkkaFeatures.IsDynamicTypeLoadingSupported)
                throw new ConfigurationException(AkkaFeatures.NotBuiltIn(
                    $"{SettingPrefix(pluginPath)}event-adapters.{adapterName}",
                    qualifiedName,
                    "an event adapter added through Akka.Persistence.Hosting (AddEventAdapter on the journal builder)"));

            return InstantiateAdapterByReflection(qualifiedName, system);
        }

        [RequiresUnreferencedCode("Loads an event adapter binding type named in HOCON by name. The trimmer cannot tell which type that is, so it may have been trimmed away.")]
        private static Type ResolveBindingTypeByReflection(string typeName)
            => Type.GetType(typeName)
               ?? throw new ConfigurationException(
                   $"Could not resolve event adapter binding type [{typeName}]. Ensure the type name is fully qualified.");

        [RequiresUnreferencedCode("Loads an event adapter type named in HOCON by name. The trimmer cannot tell which type that is, so it may have been trimmed away.")]
        private static IEventAdapter InstantiateAdapterByReflection(string qualifiedName, ExtendedActorSystem system)
        {
            var type = Type.GetType(qualifiedName, true);
            if (typeof(IEventAdapter).IsAssignableFrom(type))
                return Instantiate<IEventAdapter>(qualifiedName, system);
            if (typeof(IWriteEventAdapter).IsAssignableFrom(type))
                return new NoopReadEventAdapter(Instantiate<IWriteEventAdapter>(qualifiedName, system));
            if (typeof(IReadEventAdapter).IsAssignableFrom(type))
                return new NoopWriteEventAdapter(Instantiate<IReadEventAdapter>(qualifiedName, system));
            throw new ArgumentException("Configured " + qualifiedName + " does not implement any EventAdapter interface!");
        }

        [RequiresUnreferencedCode("Loads an event adapter type named in HOCON by name. The trimmer cannot tell which type that is, so it may have been trimmed away.")]
        private static T Instantiate<T>(string qualifiedName, ExtendedActorSystem system)
        {
            var type = Type.GetType(qualifiedName)
                ?? throw new ConfigurationException(
                    $"Could not resolve event adapter type [{qualifiedName}]. Ensure the type name is fully qualified.");
            if (!typeof(T).IsAssignableFrom(type))
                throw new ArgumentException(string.Format("Couldn't create instance of [{0}] from provided qualified type name [{1}], because it's not assignable from it",
                    typeof(T), qualifiedName));

            try
            {
                return (T)Activator.CreateInstance(type, system);
            }
            catch (MissingMethodException)
            {
                return (T)Activator.CreateInstance(type);
            }
        }

        private static IDictionary<string, string> ConfigToMap(Config config, string path)
        {
            if (config.HasPath(path))
            {
                var hoconObject = config.GetConfig(path).Root.GetObject();
                return hoconObject.Unwrapped.ToDictionary(kv => kv.Key, kv => kv.Value.ToString().Trim('"'));
            }
            else return new Dictionary<string, string> { };
        }

        private static IDictionary<string, string[]> ConfigToListMap(Config config, string path)
        {
            if (config.HasPath(path))
            {
                var hoconObject = config.GetConfig(path).Root.GetObject();
                return hoconObject.Unwrapped.ToDictionary(kv => kv.Key, kv =>
                {
                    var hoconValue = kv.Value as HoconValue;
                    if (hoconValue != null)
                    {
                        var str = hoconValue.GetString();
                        return str != null ? new[] { str } : hoconValue.GetStringList().ToArray();
                    }
                    else return new[] { kv.Value.ToString().Trim('"') };
                });
            }
            else return new Dictionary<string, string[]> { };
        }
    }
}
