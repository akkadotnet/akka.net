//-----------------------------------------------------------------------
// <copyright file="MsgPackSerializerSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2017 Akka.NET Contrib <https://github.com/AkkaNetContrib/Akka.Serialization.MessagePack>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Configuration;
using Akka.Util;
using MessagePack;

namespace Akka.Serialization.MessagePack
{
    public class MsgPackSerializerSettings
    {
        public enum Lz4Settings
        {
            None,
            Lz4Block,
            Lz4BlockArray
        }
        public MsgPackSerializerSettings(Lz4Settings enableLz4Compression) : this(
            enableLz4Compression, Array.Empty<Type>(), Array.Empty<Type>(), true,true,false)
        {
            
        }

        public Lz4Settings EnableLz4Compression { get; }
        public IEnumerable<Type> OverrideConverters { get;}
        public IEnumerable<Type> Converters { get;}
        public bool OmitAssemblyVersion { get; }
        public bool AllowAssemblyVersionMismatch { get; }
        public bool UseOldFormatterCompatibility { get; }

        public static readonly MsgPackSerializerSettings Default = new MsgPackSerializerSettings(
            enableLz4Compression: Lz4Settings.None);

        private MsgPackSerializerSettings(Lz4Settings enableLz4Compression, IEnumerable<Type> overrideConverterTypes, IEnumerable<Type> converterTypes, bool allowAssemblyVersionMismatch, bool omitAssemblyVersion, bool useOldFormatterCompatibility)
        {
            EnableLz4Compression = enableLz4Compression;
            OverrideConverters = overrideConverterTypes.ToArray();
            Converters = converterTypes.ToArray();
            AllowAssemblyVersionMismatch = allowAssemblyVersionMismatch;
            OmitAssemblyVersion = omitAssemblyVersion;
            UseOldFormatterCompatibility = useOldFormatterCompatibility;
        }

        public static MsgPackSerializerSettings Create(Config config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config), "MsgPackSerializerSettings require a config, default path: `akka.serializers.msgpack`");

            return new MsgPackSerializerSettings(
                enableLz4Compression: GetLz4Settings(config),
                GetOverrideConverterTypes(config),
                GetConverterTypes(config),
                config.GetBoolean("allow-assembly-version-mismatch", true),
                config.GetBoolean("omit-assembly-version", true),
                config.GetBoolean("use-old-formatter-compatibility", false));
        }

        private static Lz4Settings GetLz4Settings(Config config)
        {
            var str = config.GetString("enable-lz4-compression", "none");
            switch (str.ToLower())
            {
                case "lz4block":
                    return Lz4Settings.Lz4Block;
                case "lz4array":
                    return Lz4Settings.Lz4BlockArray;
                default:
                    return Lz4Settings.None;
            }
        }
        
        private static IEnumerable<Type> GetOverrideConverterTypes(Config config)
        {
            var converterNames = config.GetStringList("converters-override", new string[] { });

            if (converterNames != null)
                foreach (var converterName in converterNames)
                {
                    var type = Type.GetType(converterName, true);
                    if (!typeof(IFormatterResolver).IsAssignableFrom(type))
                        throw new ArgumentException(
                            $"Type {type} doesn't inherit from a {typeof(IFormatterResolver)}");

                    yield return type;
                }
        }
        
        private static IEnumerable<Type> GetConverterTypes(Config config)
        {
            var converterNames = config.GetStringList("converters", new string[] { });

            if (converterNames != null)
                foreach (var converterName in converterNames)
                {
                    var type = Type.GetType(converterName, true);
                    if (!typeof(IFormatterResolver).IsAssignableFrom(type))
                        throw new ArgumentException(
                            $"Type {type} doesn't inherit from a {typeof(IFormatterResolver)}");

                    yield return type;
                }
        }
    }
}
