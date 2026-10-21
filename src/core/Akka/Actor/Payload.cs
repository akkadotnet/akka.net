using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;
using Akka.Routing;
using Akka.Serialization;
using Akka.Util;
using Newtonsoft.Json;

namespace Akka.Actor
{
    public interface IPayload
    {
        int SerializerId { get; }

        string Manifest { get; }

        byte[] Data { get; }

        byte[] HashKey { get; }

        bool IsSerialized { get; }

        bool TryGetContent(ActorSystem system, out object content);
    }

    /// <summary>
    /// Container for implicit serialization, but deserialiazion is explicit 
    /// </summary>
    public sealed class Payload : IPayload, ISurrogated, IConsistentHashable, IEquatable<Payload>
    {
        readonly static JsonSerializerSettings FallbackJsonSettings = new JsonSerializerSettings
        {
            DefaultValueHandling = DefaultValueHandling.Ignore
        };

        public const int NotSerializedId = -1;
        public const int FallbackJsonSerializerId = -2;

        readonly ActorSystem _system;
        object _content;

        public int SerializerId { get; private set; } = NotSerializedId;

        public string Manifest { get; private set; }

        public byte[] Data { get; private set; }

        public byte[] HashKey { get; private set; }

        [JsonIgnore]
        public bool IsSerialized => SerializerId != NotSerializedId;

        object IConsistentHashable.ConsistentHashKey => HashKey ?? Data
            ?? GetConsistentHashKey(_content)
            ?? _content;

        public Payload(object content, ActorSystem system = null)
        {
            switch (content)
            {
                case Payload other:
                    _content = other._content;
                    _system = system ?? other._system;
                    SerializerId = other.SerializerId;
                    Manifest = other.Manifest;
                    Data = other.Data;
                    HashKey = other.HashKey;
                    break;
                case Surrogate surrogate:
                    _system = system;
                    SerializerId = surrogate.SerializerId;
                    Manifest = surrogate.Manifest;
                    Data = surrogate.Data;
                    HashKey = surrogate.HashKey;
                    break;
                default:
                    _content = content;
                    _system = system;
                    break;
            }
        }

        [JsonConstructor]
        public Payload(int serializerId, string manifest, byte[] data, byte[] hashKey)
        {
            SerializerId = serializerId;
            Manifest = manifest;
            Data = data;
            HashKey = hashKey;
        }

        private Payload(Surrogate surrogate, ActorSystem system)
        {
            _system = system;
            SerializerId = surrogate.SerializerId;
            Manifest = surrogate.Manifest;
            Data = surrogate.Data;
            HashKey = surrogate.HashKey;
        }

        public bool TryGetContent(ActorSystem system, out object content)
        {
            try
            {
                //content = (_content ??= Deserialize(system));
                if (_content is null)
                    _content = Deserialize(system);
                content = _content;
                return true;
            }
            catch (SerializationException ex)
            {
                if (IsSerialized)
                    system?.Log.Debug("Deserializing of content[{manifest}] failed: {error}",
                        Manifest, ex);

                content = null;
                return false;
            }
        }

        public Payload WithSystem(ActorSystem system)
        {
            return _system == system ? this : new Payload(this, system);
        }

        [OnSerializing]
        internal void OnSerializing(StreamingContext context)
        {
            Serialize(_system);
        }

        //[OnDeserialized]
        //internal void OnDeserialized(StreamingContext context)
        //{
        //}

        public void Serialize(ActorSystem system)
        {
            if (IsSerialized)
                return;

            if (_system is null && system is null)
            {
                //fallback serializer json
                SerializerId = FallbackJsonSerializerId;
                if (_content is null)
                {
                    Manifest = string.Empty;
                    Data = Array.Empty<byte>();
                }
                else
                {
                    Manifest = _content.GetType().TypeQualifiedName();
                    var json = FallbackToJsonString(_content);
                    Data = Encoding.UTF8.GetBytes(json);
                }
                HashKey = GetConsistentHashKey(_content);
                return;
            }

            switch ((system ?? _system).Serialization.FindSerializerFor(_content))
            {
                case SerializerWithStringManifest serial:
                    SerializerId = serial.Identifier;
                    Manifest = serial.Manifest(_content);
                    Data = serial.ToBinary(_content);
                    HashKey = GetConsistentHashKey(_content);
                    break;
                case Serializer serial when serial.IncludeManifest:
                    SerializerId = serial.Identifier;
                    Manifest = _content.GetType().TypeQualifiedName();
                    Data = serial.ToBinary(_content);
                    HashKey = GetConsistentHashKey(_content);
                    break;
                case null:
                    //fallback serializer
                    //var json = PNetConvert.ToJsonString(_content);
                    //SerializerId = -1;
                    //Data = Encoding.UTF8.GetBytes(json);
                    throw new InvalidOperationException("serializer required");
                case var serial:
                    SerializerId = serial.Identifier;
                    Manifest = string.Empty;
                    Data = serial.ToBinary(_content);
                    HashKey = GetConsistentHashKey(_content);
                    break;
            }
        }

        private object Deserialize(ActorSystem system)
        {
            switch (SerializerId)
            {
                case NotSerializedId:
                    return _content;
                case FallbackJsonSerializerId:
                    if (Data != null && Data.Length > 0)
                    {
                        var json = Encoding.UTF8.GetString(Data);
                        var type = !string.IsNullOrEmpty(Manifest)
                                ? Type.GetType(Manifest) : typeof(object);
                        return FallbackFromJsonString(json, type);
                    }
                    else
                    {
                        return null;
                    }
                default:
                    return (system ?? _system).Serialization.Deserialize(Data, SerializerId, Manifest);
            }
        }

        ISurrogate ISurrogated.ToSurrogate(ActorSystem system)
        {
            Serialize(system);

            return new Surrogate(Data, SerializerId, Manifest, HashKey);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as Payload);
        }

        public bool Equals(Payload other)
        {
            if (other is null) return false;

            if (!IsSerialized && !other.IsSerialized)
                return EqualityComparer<object>.Default.Equals(_content, other._content);

            return SerializerId == other.SerializerId &&
                   Manifest == other.Manifest &&
                   HashKey.AsSpan().SequenceEqual(other.HashKey) &&
                   Data.AsSpan().SequenceEqual(other.Data);
        }

        public override int GetHashCode()
        {
            //return HashCode.Combine(HashKey);
            return ConsistentHash.HashFor(HashKey);
        }

        static byte[] GetConsistentHashKey(object obj)
        {
            if (ConsistentHash.ToBytesOrObject(obj) is byte[] bytes)
                return bytes;

            //todo system default hash mapping insteed of empty
            var mappedKey = ConsistentHashingRouter.EmptyConsistentHashMapping(obj);
            if (mappedKey != null)
                return ConsistentHash.ToBytesOrObject(mappedKey) as byte[];
            if (obj is IConsistentHashable hashable)
                return ConsistentHash.ToBytesOrObject(hashable.ConsistentHashKey) as byte[];

            return null;
        }

        static object FallbackFromJsonString(string json, Type type)
        {
            return !string.IsNullOrEmpty(json)
                   ? JsonConvert.DeserializeObject(json, type, FallbackJsonSettings)
                   : null;
        }

        static string FallbackToJsonString<T>(T value)
        {
            return value != null
                ? JsonConvert.SerializeObject(value, FallbackJsonSettings)
                : string.Empty;
        }

        internal sealed class Surrogate : ISurrogate
        {
            public readonly int SerializerId;

            public readonly string Manifest;

            public readonly byte[] Data;

            public readonly byte[] HashKey;

            public Surrogate(byte[] data, int serializerId, string manifest, byte[] hashKey)
            {
                SerializerId = serializerId;
                Manifest = manifest;
                Data = data;
                HashKey = hashKey;
            }

            ISurrogated ISurrogate.FromSurrogate(ActorSystem system)
            {
                return new Payload(this, system);
            }
        }
    }
}
