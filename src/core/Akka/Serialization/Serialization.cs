//-----------------------------------------------------------------------
// <copyright file="Serialization.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Configuration.Hocon;
using Akka.Util.Internal;

namespace Akka.Serialization
{
    /// <summary>
    /// TBD
    /// </summary>
    public class Information
    {
        /// <summary>
        /// TBD
        /// </summary>
        public Address Address { get; set; }
        /// <summary>
        /// TBD
        /// </summary>
        public ActorSystem System { get; set; }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public class Serialization
    {
        [ThreadStatic]
        private static Information _currentTransportInformation;

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="system">TBD</param>
        /// <param name="address">TBD</param>
        /// <param name="action">TBD</param>
        /// <returns>TBD</returns>
        public static T SerializeWithTransport<T>(ActorSystem system, Address address, Func<T> action)
        {
            _currentTransportInformation = new Information()
            {
                System = system,
                Address = address
            };
            var res = action();
            _currentTransportInformation = null;
            return res;
        }

        private readonly Serializer _nullSerializer;

        private readonly Dictionary<Type, Serializer> _serializerMap = new Dictionary<Type, Serializer>();
        private readonly Dictionary<int, Serializer> _serializers = new Dictionary<int, Serializer>();

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        public Serialization(ExtendedActorSystem system)
        {
            System = system;

            _nullSerializer = new NullSerializer(system);
            _serializers.Add(_nullSerializer.Identifier, _nullSerializer);

            var serializersConfig = system.Settings.Config.GetConfig("akka.actor.serializers").AsEnumerable().ToList();
            var serializerBindingConfig = system.Settings.Config.GetConfig("akka.actor.serialization-bindings").AsEnumerable().ToList();
            var serializerSettingsConfig = system.Settings.Config.GetConfig("akka.actor.serialization-settings");
            var namedSerializers = new Dictionary<string, Serializer>();
            foreach (var kvp in serializersConfig)
            {
                var serializerTypeName = kvp.Value.GetString();
                var serializerType = Type.GetType(serializerTypeName);
                if (serializerType == null)
                {
                    system.Log.Warning("The type name for serializer '{0}' did not resolve to an actual Type: '{1}'", kvp.Key, serializerTypeName);
                    continue;
                }

                var serializerConfig = serializerSettingsConfig.GetConfig(kvp.Key);

                var serializer = serializerConfig != null
                    ? (Serializer)Activator.CreateInstance(serializerType, system, serializerConfig)
                    : (Serializer)Activator.CreateInstance(serializerType, system);
                _serializers.Add(serializer.Identifier, serializer);
                namedSerializers.Add(kvp.Key, serializer);
            }

            foreach (var kvp in serializerBindingConfig)
            {
                var typename = kvp.Key;
                var serializerName = kvp.Value.GetString();
                var messageType = Type.GetType(typename);

                if (messageType == null)
                {

                    system.Log.Warning("The type name for message/serializer binding '{0}' did not resolve to an actual Type: '{1}'", serializerName, typename);
                    continue;
                }

                Serializer serializer;

                if (!namedSerializers.TryGetValue(serializerName, out serializer))
                {
                    system.Log.Warning("Serialization binding to non existing serializer: '{0}'", serializerName);
                    continue;
                }

                _serializerMap.Add(messageType, serializer);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public ActorSystem System { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="serializer">TBD</param>
        public void AddSerializer(Serializer serializer)
        {
            _serializers.Add(serializer.Identifier, serializer);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="type">TBD</param>
        /// <param name="serializer">TBD</param>
        public void AddSerializationMap(Type type, Serializer serializer)
        {
            _serializerMap.Add(type, serializer);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="bytes">TBD</param>
        /// <param name="serializerId">TBD</param>
        /// <param name="type">TBD</param>
        /// <exception cref="SerializationException">
        /// This exception is thrown if the system cannot find the serializer with the given <paramref name="serializerId"/>.
        /// </exception>
        /// <returns>TBD</returns>
        public object Deserialize(byte[] bytes, int serializerId, Type type)
        {
            Serializer serializer;
            if (!_serializers.TryGetValue(serializerId, out serializer))
                throw new SerializationException(
                    $"Cannot find serializer with id [{serializerId}]. The most probable reason" +
                    " is that the configuration entry 'akka.actor.serializers' is not in sync between the two systems.");
            
            return serializer.FromBinary(bytes, type);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="bytes">TBD</param>
        /// <param name="serializerId">TBD</param>
        /// <param name="manifest">TBD</param>
        /// <exception cref="SerializationException">
        /// This exception is thrown if the system cannot find the serializer with the given <paramref name="serializerId"/>
        /// or it couldn't find the given <paramref name="manifest"/> with the given <paramref name="serializerId"/>.
        /// </exception>
        /// <returns>TBD</returns>
        public object Deserialize(byte[] bytes, int serializerId, string manifest)
        {
            Serializer serializer;
            if (!_serializers.TryGetValue(serializerId, out serializer))
                throw new SerializationException(
                    $"Cannot find serializer with id [{serializerId}]. The most probable reason" +
                    " is that the configuration entry 'akka.actor.serializers' is not in sync between the two systems.");
 
            if (serializer is SerializerWithStringManifest)
                return ((SerializerWithStringManifest)serializer).FromBinary(bytes, manifest);
            if (string.IsNullOrEmpty(manifest))
                return serializer.FromBinary(bytes, null);
            Type type;
            try
            {
                type = Type.GetType(manifest);
            }
            catch
            {
                throw new SerializationException($"Cannot find manifest class [{manifest}] for serializer with id [{serializerId}].");
            }
            return serializer.FromBinary(bytes, type);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="obj">TBD</param>
        /// <returns>TBD</returns>
        public Serializer FindSerializerFor(object obj)
        {
            if (obj == null)
                return _nullSerializer;

            Type type = obj.GetType();
            return FindSerializerForType(type);
        }

        //cache to eliminate lots of typeof operator calls
        private readonly Type _objectType = typeof(object);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="objectType">TBD</param>
        /// <exception cref="SerializationException">
        /// This exception is thrown if the serializer of the given <paramref name="objectType"/> could not be found.
        /// </exception>
        /// <returns>TBD</returns>
        public Serializer FindSerializerForType(Type objectType)
        {
            Type type = objectType;
            //TODO: see if we can do a better job with proper type sorting here - most specific to least specific (object serializer goes last)
            foreach (var serializerType in _serializerMap)
            {
                //force deferral of the base "object" serializer until all other higher-level types have been evaluated
                if (serializerType.Key.IsAssignableFrom(type) && serializerType.Key != _objectType)
                    return serializerType.Value;
            }

            //do a final check for the "object" serializer
            if (_serializerMap.ContainsKey(_objectType) && _objectType.IsAssignableFrom(type))
                return _serializerMap[_objectType];

            throw new SerializationException($"Serializer not found for type {objectType.Name}");
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="actorRef">TBD</param>
        /// <returns>TBD</returns>
        public static string SerializedActorPath(IActorRef actorRef)
        {
            if (Equals(actorRef, ActorRefs.NoSender))
                return String.Empty;

            var path = actorRef.Path;
            ExtendedActorSystem originalSystem = null;
            if (actorRef is ActorRefWithCell)
            {
                originalSystem = actorRef.AsInstanceOf<ActorRefWithCell>().Underlying.System.AsInstanceOf<ExtendedActorSystem>();
            }

            if (_currentTransportInformation == null)
            {
                if (originalSystem == null)
                {
                    var res = path.ToSerializationFormat();
                    return res;
                }
                else
                {
                    var defaultAddress = originalSystem.Provider.DefaultAddress;
                    var res = path.ToSerializationFormatWithAddress(defaultAddress);
                    return res;
                }
            }

            //CurrentTransportInformation exists
            var system = _currentTransportInformation.System;
            var address = _currentTransportInformation.Address;
            if (originalSystem == null || originalSystem == system)
            {
                var res = path.ToSerializationFormatWithAddress(address);
                return res;
            }
            else
            {
                var provider = originalSystem.Provider;
                var res =
                    path.ToSerializationFormatWithAddress(provider.GetExternalAddressFor(address).GetOrElse(provider.DefaultAddress));
                return res;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="serializerId">TBD</param>
        /// <returns>TBD</returns>
        public Serializer GetSerializerById(int serializerId)
        {
            return _serializers[serializerId];
        }
    }
}
