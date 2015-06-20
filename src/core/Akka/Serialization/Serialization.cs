//-----------------------------------------------------------------------
// <copyright file="Serialization.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Actor.Internals;
using Akka.Util.Internal;

namespace Akka.Serialization
{
    public class Information
    {
        public Address Address { get; set; }
        public ActorSystem System { get; set; }
    }

    public class Serialization
    {
        [ThreadStatic] public static Information CurrentTransportInformation;

        private readonly Serializer _nullSerializer;

        private readonly Dictionary<Type, Serializer> _serializerMap = new Dictionary<Type, Serializer>();
        private readonly Dictionary<int, Serializer> _serializers = new Dictionary<int, Serializer>();

        public Serialization(ExtendedActorSystem system)
        {
            System = system;

            _nullSerializer = new NullSerializer(system);
            _serializers.Add(_nullSerializer.Identifier,_nullSerializer);

            var serializersConfig = system.Settings.Config.GetConfig("akka.actor.serializers").AsEnumerable().ToList();
            var serializerBindingConfig = system.Settings.Config.GetConfig("akka.actor.serialization-bindings").AsEnumerable().ToList();
            var namedSerializers = new Dictionary<string, Serializer>();
            foreach (var kvp in serializersConfig)
            {
                var serializerTypeName = kvp.Value.GetString();
                var serializerType = Type.GetType(serializerTypeName);
                if (serializerType == null)
                {
                    system.Log.Warning("The type name for serializer '{0}' did not resolve to an actual Type: '{1}'",kvp.Key, serializerTypeName);
                    continue;
                }

                var serializer = (Serializer)Activator.CreateInstance(serializerType,system);
                _serializers.Add(serializer.Identifier, serializer);
                namedSerializers.Add(kvp.Key,serializer);
            }

            foreach (var kvp in serializerBindingConfig)
            {
                var typename = kvp.Key;
                var serializerName = kvp.Value.GetString();
                var messageType = Type.GetType(typename);

                if (messageType == null)
                {

                    system.Log.Warning("The type name for message/serializer binding '{0}' did not resolve to an actual Type: '{1}'",serializerName, typename);
                    continue;
                }

                var serializer = namedSerializers[serializerName];
                if (serializer == null)
                {
                    system.Log.Warning("Serialization binding to non existing serializer: '{0}'", serializerName);
                    continue;
                }
                _serializerMap.Add(messageType,serializer);
            }
        }

        public ActorSystem System { get; private set; }

        public void AddSerializer(Serializer serializer)
        {
            _serializers.Add(serializer.Identifier, serializer);
        }

        public void AddSerializationMap(Type type, Serializer serializer)
        {
            _serializerMap.Add(type, serializer);
        }

        public object Deserialize(byte[] bytes, int serializerId, Type type)
        {
            return _serializers[serializerId].FromBinary(bytes, type);
        }

        public Serializer FindSerializerFor(object obj)
        {
            if (obj == null)
                return _nullSerializer;

            Type type = obj.GetType();
            return FindSerializerForType(type);
        }

        //cache to eliminate lots of typeof operator calls
        private readonly Type _objectType = typeof (object);
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

            throw new Exception("Serializer not found for type " + objectType.Name);
        }

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

            if (CurrentTransportInformation == null)
            {
                if (originalSystem == null)
                {
                    var res = path.ToSerializationFormat();
                    return res;
                }
                else
                {
                    var defaultAddress = originalSystem.Provider.DefaultAddress;
                    var res = path.ToStringWithAddress(defaultAddress);
                    return res;
                }
            }

            //CurrentTransportInformation exists
            var system = CurrentTransportInformation.System;
            var address = CurrentTransportInformation.Address;
            if (originalSystem == null || originalSystem == system)
            {
                var res = path.ToStringWithAddress(address);
                return res;
            }
            else
            {
                var provider = originalSystem.Provider;
                var res =
                    path.ToStringWithAddress(provider.GetExternalAddressFor(address).GetOrElse(provider.DefaultAddress));
                return res;
            }
        }

        public Serializer GetSerializerById(int serializerId)
        {
            return _serializers[serializerId];
        }
    }
}

