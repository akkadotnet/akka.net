//-----------------------------------------------------------------------
// <copyright file="MsgPackSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2017 Akka.NET Contrib <https://github.com/AkkaNetContrib/Akka.Serialization.MessagePack>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Akka.Actor;
using Akka.Configuration;
using Akka.Serialization.MessagePack.Resolvers;
using MessagePack;
using MessagePack.Formatters;
using MessagePack.ImmutableCollection;
using MessagePack.Resolvers;
using Newtonsoft.Json;

namespace Akka.Serialization.MessagePack
{
    public sealed class MsgPackSerializer : Serializer
    {
        private readonly MsgPackSerializerSettings _settings;
        private readonly IFormatterResolver _resolver;
        public readonly MessagePackSerializerOptions SerializerOptions;
        private readonly IFormatterResolver _polymorphicResolver;

        public MsgPackSerializer(ExtendedActorSystem system) : this(system, MsgPackSerializerSettings.Default)
        {

        }

        public MsgPackSerializer(ExtendedActorSystem system, Config config) 
            : this(system, MsgPackSerializerSettings.Create(config))
        {
        }
        
        

        ///<remarks>
        /// Borrowed from <see cref="NewtonSoftJsonSerializer"/>,
        /// The concept is very similar if slightly more layered for perf.
        /// </remarks>
        public static IFormatterResolver LoadFormatterResolverByType(Type type, ExtendedActorSystem system)
        {
            //This -should- be double checked by others, but just in case :)
            if (typeof(IFormatterResolver).IsAssignableFrom(type))
            {
                //We look In this order:
                // - Is there a Ctor that will take ActorSystem/ExtendedActorSystem?
                // - Is there a Static 'Instance' Property/Field?
                // - Is there a Public, Parameterless Ctor?
                
                var ctors = type.GetConstructors();
                var actorSystemCtorMaybe = ctors.FirstOrDefault(r =>
                {
                    var p = r.GetParameters();
                    if (p.Length == 1 && p[0].ParameterType
                            .IsAssignableFrom(typeof(ExtendedActorSystem)))
                    {
                        return true;
                    }

                    return false;
                });
                if (actorSystemCtorMaybe != null)
                {
                    return (IFormatterResolver)actorSystemCtorMaybe.Invoke(new[]
                        { system });
                }
                
                var props = type.GetProperties(BindingFlags.Static | BindingFlags.Public);
                foreach (var propertyInfo in props)
                {
                    if (propertyInfo.Name == "Instance")
                    {
                        return (IFormatterResolver)propertyInfo.GetValue(null);
                    }
                }
                var fields = type.GetFields(BindingFlags.Static | BindingFlags.Public);
                foreach (var fieldInfo in fields)
                {
                    if (fieldInfo.Name == "Instance")
                    {
                        return (IFormatterResolver)fieldInfo.GetValue(null);
                    }
                }

                var defaultCtor =
                    ctors.FirstOrDefault(r => r.GetParameters().Length == 0);
                if (defaultCtor != null)
                {
                    return (IFormatterResolver)defaultCtor.Invoke(Array.Empty<object>());
                }

                throw new ArgumentException(
                    $"Type {type} does not contain a static 'Instance' Property/Field, Ctor that takes ActorSystem, or Parameterless Ctor!");
            }
            else
            {
                throw new ArgumentException(
                    $"Type {type} is not assignable to IMessageFormatter!");
            }
        }

        public MsgPackSerializer(ExtendedActorSystem system,
            MsgPackSerializerSettings settings) : base(system)
        {
            _settings = settings;
            //Set up the chain of resolvers;
            //First, we allow 'Overrides' that users put in at their own peril.
            //Then, we load our standard set of converters, which includes
            //Serializable(really just exceptions),
            //Immutable Collections, and our Surrogate Resolver
            //Then we add whatever custom serializers are specified,
            //Lastly dropping into the TypelessContractLess Resolver.
            //
            _resolver = CompositeResolver.Create(
                _settings.OverrideConverters.Select(t =>
                        LoadFormatterResolverByType(t, system))
                    .Concat(new[]
                    {
                        SerializableResolver.Instance,
                        ImmutableCollectionResolver.Instance,
                        settings.UseOldFormatterCompatibility
                            ? (IFormatterResolver)new
                                BackwardsCompatibleSurrogatedFormatterResolver(
                                    base.system)
                            : new SurrogatedFormatterResolver(system)
                    })
                    .Concat(_settings.Converters.Select(t =>
                        LoadFormatterResolverByType(t, system)))
                    .Concat(new[]
                    {
                        TypelessContractlessStandardResolver.Instance
                    })
                    .ToArray());
            _polymorphicResolver = new PolymorphicFormatterResolver(_resolver);
            var opts =
                new MessagePackSerializerOptions(_polymorphicResolver);
            if (_settings.EnableLz4Compression == MsgPackSerializerSettings.Lz4Settings.Lz4Block)
            {
                opts = opts.WithCompression(MessagePackCompression.Lz4Block);
            }
            else if (_settings.EnableLz4Compression ==
                     MsgPackSerializerSettings.Lz4Settings.Lz4BlockArray)
            {
                opts = opts.WithCompression(
                    MessagePackCompression.Lz4BlockArray);
            }

            opts = opts.WithAllowAssemblyVersionMismatch(_settings
                .AllowAssemblyVersionMismatch);
            opts = opts.WithOmitAssemblyVersion(_settings.OmitAssemblyVersion);
            //We handle type filtering via our own options set.
            //By doing so, the existing Typeless API will hook in,
            //i.e. we don't have to write our own Typeless Filter.
            SerializerOptions = new MessagePackTypeFilteringOptions(opts);

        }

        public override byte[] ToBinary(object obj)
        {
            {
                return MessagePackSerializer.Serialize(obj.GetType(), obj,SerializerOptions);
            }
        }

        public override object FromBinary(byte[] bytes, Type type)
        {
            return MessagePackSerializer.Deserialize(type, bytes,SerializerOptions);
        }

        public override object FromBinary(ReadOnlyMemory<byte> bytes, Type type)
        {
            return MessagePackSerializer.Deserialize(type, bytes, SerializerOptions);
        }

        public override int Identifier => 151;

        public override bool IncludeManifest => true;
        
    }
}
