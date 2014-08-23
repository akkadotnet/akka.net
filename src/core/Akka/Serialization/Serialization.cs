using System;

using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;

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

        [ThreadStatic] public static ActorSystem CurrentSystem;
        private readonly Serializer _nullSerializer;

        private readonly Dictionary<Type, Serializer> _serializerMap = new Dictionary<Type, Serializer>();
        private readonly Dictionary<int, Serializer> _serializers = new Dictionary<int, Serializer>();
        //private Serializer byteArraySerializer;
        //private Serializer javaSerializer;
        //private Serializer newtonsoftJsonSerializer;

        private Config _serializersConfig;
        private Config _serializerBindingConfig;

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
                    throw new ConfigurationException(
                        string.Format("The type name for serializer '{0}' did not resolve to an actual Type: '{1}'",
                            kvp.Key, serializerTypeName));

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
                    throw new ConfigurationException(
                        string.Format("The type name for message/serializer binding '{0}' did not resolve to an actual Type: '{1}'",
                            serializerName, messageType));

                var serializer = namedSerializers[serializerName];
                _serializerMap.Add(messageType,serializer);
            }


            //newtonsoftJsonSerializer = new NewtonSoftJsonSerializer(system);




            ////protobufnetSerializer = new ProtoBufNetSerializer(system);
            ////jsonSerializer = new FastJsonSerializer(system);
            //javaSerializer = new JavaSerializer(system);
            
            //byteArraySerializer = new ByteArraySerializer(system);

            //serializers.Add(newtonsoftJsonSerializer.Identifier, newtonsoftJsonSerializer);
            ////serializers.Add(protobufnetSerializer.Identifier, protobufnetSerializer);
            ////serializers.Add(jsonSerializer.Identifier, jsonSerializer);
            //serializers.Add(javaSerializer.Identifier, javaSerializer);
            //serializers.Add(nullSerializer.Identifier, nullSerializer);
            //serializers.Add(byteArraySerializer.Identifier, byteArraySerializer);

            //serializerMap.Add(typeof (object), newtonsoftJsonSerializer);
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
            //if (obj is byte[])
            //    return byteArraySerializer;

            Type type = obj.GetType();
            return FindSerializerForType(type);
        }

        public Serializer FindSerializerForType(Type objectType)
        {
            Type type = objectType;
            while (type != null)
            {
                if (_serializerMap.ContainsKey(type))
                    return _serializerMap[type];
                type = type.BaseType;
            }
            throw new Exception("Serializer not found for type " + objectType.Name);
        }

        public static string SerializedActorPath(ActorRef @ref)
        {
            /*
val path = actorRef.path
    val originalSystem: ExtendedActorSystem = actorRef match {
      case a: ActorRefWithCell ⇒ a.underlying.system.asInstanceOf[ExtendedActorSystem]
      case _                   ⇒ null
    }
    Serialization.currentTransportInformation.value match {
      case null ⇒ originalSystem match {
        case null ⇒ path.toSerializationFormat
        case system ⇒
          try path.toSerializationFormatWithAddress(system.provider.getDefaultAddress)
          catch { case NonFatal(_) ⇒ path.toSerializationFormat }
      }
      case Information(address, system) ⇒
        if (originalSystem == null || originalSystem == system)
          path.toSerializationFormatWithAddress(address)
        else {
          val provider = originalSystem.provider
          path.toSerializationFormatWithAddress(provider.getExternalAddressFor(address).getOrElse(provider.getDefaultAddress))
        }
    }*/
            ActorSystem originalSystem = null;
            if (@ref is ActorRefWithCell)
            {
                originalSystem = @ref.AsInstanceOf<ActorRefWithCell>().Underlying.System;
                if (CurrentTransportInformation == null)
                {
                    return @ref.Path.ToSerializationFormat();
                }
                return @ref.Path.ToStringWithAddress(CurrentTransportInformation.Address);
            }
            return @ref.Path.ToSerializationFormat();
        }

        public Serializer GetSerializerById(int serializerId)
        {
            return _serializers[serializerId];
        }
    }
}