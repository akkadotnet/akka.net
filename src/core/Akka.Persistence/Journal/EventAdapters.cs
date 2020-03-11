//-----------------------------------------------------------------------
// <copyright file="EventAdapters.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Akka.Actor;
using Akka.Configuration;
using Akka.Configuration.Hocon;
using Akka.Event;
using Akka.Pattern;

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
        /// <param name="evt">TBD</param>
        /// <returns>TBD</returns>
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
        public static IdentityEventAdapter Instance { get; } = new IdentityEventAdapter();

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
    /// TBD
    /// </summary>
    [Serializable]
    public sealed class CombinedReadEventAdapter : IEventAdapter
    {
        private static readonly Exception OnlyReadSideException = new IllegalStateException(
                "CombinedReadEventAdapter must not be used when writing (creating manifests) events!");

        /// <summary>
        /// TBD
        /// </summary>
        public IEnumerable<IEventAdapter> Adapters { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="adapters">TBD</param>
        public CombinedReadEventAdapter(IEnumerable<IEventAdapter> adapters)
        {
            Adapters = adapters.ToArray();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="evt">TBD</param>
        /// <exception cref="IllegalStateException">TBD</exception>
        /// <returns>TBD</returns>
        public string Manifest(object evt)
        {
            throw OnlyReadSideException;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="evt">TBD</param>
        /// <exception cref="IllegalStateException">TBD</exception>
        /// <returns>TBD</returns>
        public object ToJournal(object evt)
        {
            throw OnlyReadSideException;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="evt">TBD</param>
        /// <param name="manifest">TBD</param>
        /// <returns>TBD</returns>
        public IEventSequence FromJournal(object evt, string manifest)
        {
            return EventSequence.Create(Adapters.SelectMany(adapter => adapter.FromJournal(evt, manifest).Events));
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    internal class IdentityEventAdapters : EventAdapters
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly EventAdapters Instance = new IdentityEventAdapters();

        private IdentityEventAdapters() : base(null, null, null)
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="type">TBD</param>
        /// <returns>TBD</returns>
        public override IEventAdapter Get(Type type)
        {
            return IdentityEventAdapter.Instance;
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public class EventAdapters
    {
        private readonly ConcurrentDictionary<Type, IEventAdapter> _map;
        private readonly IEnumerable<KeyValuePair<Type, IEventAdapter>> _bindings;
        private readonly ILoggingAdapter _log;

        /// <summary>
        /// Initializes a new instance of the <see cref="EventAdapters"/> class.
        /// </summary>
        /// <param name="system">TBD</param>
        /// <param name="config">TBD</param>
        /// <returns>TBD</returns>
        public static EventAdapters Create(ExtendedActorSystem system, Config config)
        {
            var adapters = ConfigToMap(config, "event-adapters");
            var adapterBindings = ConfigToListMap(config, "event-adapter-bindings");

            return Create(system, adapters, adapterBindings);
        }

        private static EventAdapters Create(ExtendedActorSystem system, IDictionary<string, string> adapters, IDictionary<string, string[]> adapterBindings)
        {
            var adapterNames = new HashSet<string>(adapters.Keys);
            foreach (var kv in adapterBindings)
            {
                foreach (var boundAdapter in kv.Value)
                {
                    if (!adapterNames.Contains(boundAdapter))
                        throw new ArgumentException(string.Format("{0} was bound to undefined event-adapter: {1} (bindings: [{2}], known adapters: [{3}])",
                            kv.Key, boundAdapter, string.Join(", ", kv.Value), string.Join(", ", adapters.Keys)));
                }
            }

            // A Map of handler from alias to implementation (i.e. class implementing Akka.Serialization.ISerializer)
            // For example this defines a handler named 'country': `"country" -> com.example.comain.CountryTagsAdapter`
            var handlers = adapters.ToDictionary(kv => kv.Key, kv => InstantiateAdapter(kv.Value, system));

            // bindings is a enumerable of key-val representing the mapping from Type to handler.
            // It is primarily ordered by the most specific classes first, and secondly in the configured order.
            var bindings = Sort(adapterBindings.Select(kv =>
            {
                var type = Type.GetType(kv.Key);
                var adapter = kv.Value.Length == 1
                    ? handlers[kv.Value[0]]
                    : new NoopWriteEventAdapter(new CombinedReadEventAdapter(kv.Value.Select(h => handlers[h])));
                return new KeyValuePair<Type, IEventAdapter>(type, adapter);
            }).ToList());

            var backing = new ConcurrentDictionary<Type, IEventAdapter>();

            foreach (var pair in bindings)
            {
                backing.AddOrUpdate(pair.Key, pair.Value, (type, adapter) => pair.Value);
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

        private static int IndexWhere<T>(IList<T> list, Predicate<T> predicate)
        {
            for (int i = 0; i < list.Count; i++)
                if (predicate(list[i])) return i;

            return -1;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="EventAdapters"/> class.
        /// </summary>
        /// <param name="map">TBD</param>
        /// <param name="bindings">TBD</param>
        /// <param name="log">TBD</param>
        protected EventAdapters(ConcurrentDictionary<Type, IEventAdapter> map, IEnumerable<KeyValuePair<Type, IEventAdapter>> bindings, ILoggingAdapter log)
        {
            _map = map;
            _bindings = bindings;
            _log = log;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <returns>TBD</returns>
        public IEventAdapter Get<T>()
        {
            return Get(typeof(T));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="type">TBD</param>
        /// <returns>TBD</returns>
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

        private static IEventAdapter InstantiateAdapter(string qualifiedName, ExtendedActorSystem system)
        {
            var type = Type.GetType(qualifiedName, true);
            if (typeof(IEventAdapter).IsAssignableFrom(type))
                return Instantiate<IEventAdapter>(qualifiedName, system);
            if (typeof (IWriteEventAdapter).IsAssignableFrom(type))
                return new NoopReadEventAdapter(Instantiate<IWriteEventAdapter>(qualifiedName, system));
            if (typeof (IReadEventAdapter).IsAssignableFrom(type))
                return new NoopWriteEventAdapter(Instantiate<IReadEventAdapter>(qualifiedName, system));
            throw new ArgumentException("Configured " + qualifiedName + " does not implement any EventAdapter interface!");
        }

        private static T Instantiate<T>(string qualifiedName, ExtendedActorSystem system)
        {
            var type = Type.GetType(qualifiedName);
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
