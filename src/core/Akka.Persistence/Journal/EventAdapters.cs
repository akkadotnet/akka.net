//-----------------------------------------------------------------------
// <copyright file="EventAdapters.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Configuration.Hocon;
using Akka.Event;
using Akka.Pattern;

namespace Akka.Persistence.Journal
{
    /// <summary>
    /// <para>Facility to convert from and to specialised data models, as may be required by specialized persistence Journals.</para>
    ///
    /// <para>Typical use cases include(but are not limited to):</para>
    /// <para>- adding metadata, a.k.a. "tagging" - by wrapping objects into tagged counterparts</para>
    /// <para>- manually converting to the Journals storage format, such as JSON, BSON or any specialised binary format</para>
    /// <para>- adapting incoming events in any way before persisting them by the journal</para>
    /// </summary>
    public interface IEventAdapter
    {
        /// <summary>
        /// Return the manifest (type hint) that will be provided in the <see cref="FromJournal"/> method. Use empty string if not needed.
        /// </summary>
        /// <param name="evt"></param>
        /// <returns></returns>
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

        /// <summary>
        /// <para>Convert a event from its journal model to the applications domain model.</para>
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

    [Serializable]
    public class IdentityEventAdapter : IEventAdapter
    {
        public static readonly IdentityEventAdapter Instance = new IdentityEventAdapter();

        private IdentityEventAdapter() { }

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
            return EventSequence.Single(evt);
        }
    }

    [Serializable]
    public sealed class CombinedReadEventAdapter : IEventAdapter
    {
        private static readonly Exception OnlyReadSideException = new IllegalStateException(
                "CombinedReadEventAdapter must not be used when writing (creating manifests) events!");

        private readonly IEventAdapter[] _adapters;

        public IEnumerable<IEventAdapter> Adapters { get { return _adapters; } }

        public CombinedReadEventAdapter(IEnumerable<IEventAdapter> adapters)
        {
            _adapters = adapters.ToArray();
        }

        public string Manifest(object evt)
        {
            throw OnlyReadSideException;
        }

        public object ToJournal(object evt)
        {
            throw OnlyReadSideException;
        }

        public IEventSequence FromJournal(object evt, string manifest)
        {
            return EventSequence.Create(_adapters.SelectMany(adapter => adapter.FromJournal(evt, manifest).Events));
        }
    }

    internal class IdentityEventAdapters : EventAdapters
    {
        public static readonly EventAdapters Instance = new IdentityEventAdapters();

        private IdentityEventAdapters() : base(null, null, null)
        {
        }

        public override IEventAdapter Get(Type type)
        {
            return IdentityEventAdapter.Instance;
        }
    }

    public class EventAdapters
    {
        private readonly ConcurrentDictionary<Type, IEventAdapter> _map;
        private readonly IEnumerable<KeyValuePair<Type, IEventAdapter>> _bindings;
        private readonly ILoggingAdapter _log;

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
            var handlers = adapters.ToDictionary(kv => kv.Key, kv => Instantiate<IEventAdapter>(kv.Value, system));

            // bindings is a enumerable of key-val representing the mapping from Type to handler.
            // It is primarily ordered by the most specific classes first, and secondly in the configured order.

            var bindings = Sort(adapterBindings.Select(kv =>
            {
                try
                {
                    var type = Type.GetType(kv.Key, true);
                    var adapter = kv.Value.Length == 1
                            ? handlers[kv.Value[0]]
                            : new CombinedReadEventAdapter(kv.Value.Select(h => handlers[h]));
                    return new KeyValuePair<Type, IEventAdapter>(type, adapter);
                }
                catch (Exception ex)
                {
                    var msgException = string.Format("Could not find type '{0}'", kv.Key);
                    throw new TypeLoadException(msgException, ex);
                }
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

        protected EventAdapters(ConcurrentDictionary<Type, IEventAdapter> map, IEnumerable<KeyValuePair<Type, IEventAdapter>> bindings, ILoggingAdapter log)
        {
            _map = map;
            _bindings = bindings;
            _log = log;
        }

        public IEventAdapter Get<T>()
        {
            return Get(typeof(T));
        }

        public virtual IEventAdapter Get(Type type)
        {
            IEventAdapter adapter;
            if (_map.TryGetValue(type, out adapter))
                return adapter;

            // bindings are ordered from most specific to least specific
            var pair = _bindings.FirstOrDefault(kv => kv.Key.IsAssignableFrom(type));
            var value = !pair.Equals(default(KeyValuePair<Type, IEventAdapter>)) ? pair.Value : IdentityEventAdapter.Instance;

            adapter = _map.GetOrAdd(type, value);
            return adapter;
        }

        private static T Instantiate<T>(string qualifiedName, ExtendedActorSystem system)
        {
            try
            {
                var instanceType = Type.GetType(qualifiedName, true);
                try
                {
                    if (!typeof(T).IsAssignableFrom(instanceType))
                        throw new ArgumentException(string.Format("Couldn't create instance of [{0}] from provided qualified type name [{1}], because it's not assignable from it",
                            typeof(T), qualifiedName));

                    return (T)Activator.CreateInstance(instanceType, system);
                }
                catch (MissingMethodException)
                {
                    return (T)Activator.CreateInstance(instanceType);
                }
            }
            catch (TypeLoadException ex)
            {
                var msgException = string.Format("Could not find type '{0}'", qualifiedName);
                throw new TypeLoadException(msgException, ex);
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