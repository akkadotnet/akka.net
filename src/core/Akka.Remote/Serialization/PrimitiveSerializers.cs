//-----------------------------------------------------------------------
// <copyright file="PrimitiveSerializers.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Text;
using Akka.Actor;
using Akka.Serialization;
using Akka.Util;

namespace Akka.Remote.Serialization
{
    public sealed class PrimitiveSerializers : SerializerWithStringManifest
    {
        internal const string StringManifest = "S";
        internal const string Int32Manifest = "I";
        internal const string Int64Manifest = "L";

        // .Net Core manifests
        internal const string StringManifestNetCore = "System.String, System.Private.CoreLib";
        internal const string Int32ManifestNetCore = "System.Int32, System.Private.CoreLib";
        internal const string Int64ManifestNetCore = "System.Int64, System.Private.CoreLib";

        // .Net Framework manifests
        internal const string StringManifestNetFx = "System.String, mscorlib";
        internal const string Int32ManifestNetFx = "System.Int32, mscorlib";
        internal const string Int64ManifestNetFx = "System.Int64, mscorlib";

        /// <summary>
        /// Initializes a new instance of the <see cref="PrimitiveSerializers" /> class.
        /// </summary>
        /// <param name="system">The actor system to associate with this serializer. </param>
        public PrimitiveSerializers(ExtendedActorSystem system) : base(system)
        {
        }

        /// <inheritdoc />
        public override byte[] ToBinary(object obj)
        {
            switch (obj)
            {
                case string s:
                    return Encoding.UTF8.GetBytes(s);
                case int i:
                    return BitConverter.GetBytes(i);
                case long l:
                    return BitConverter.GetBytes(l);
                default:
                    throw new ArgumentException($"Cannot serialize object of type [{obj.GetType()}]");
            }
        }

        /// <inheritdoc />
        public override object FromBinary(byte[] bytes, string manifest)
        {
            switch (manifest)
            {
                case StringManifest:
                case StringManifestNetCore:
                case StringManifestNetFx:
                    return Encoding.UTF8.GetString(bytes);
                case Int32Manifest:
                case Int32ManifestNetCore:
                case Int32ManifestNetFx:
                    return BitConverter.ToInt32(bytes, 0);
                case Int64Manifest:
                case Int64ManifestNetCore:
                case Int64ManifestNetFx:
                    return BitConverter.ToInt64(bytes, 0);
                default:
                    throw new ArgumentException($"Unimplemented deserialization of message with manifest [{manifest}] in [${GetType()}]");
            }
        }

        /// <inheritdoc />
        public override string Manifest(object obj)
        {
            switch (obj)
            {
                case string _:
                    return StringManifest;
                case int _:
                    return Int32Manifest;
                case long _:
                    return Int64Manifest;
                default:
                    throw new ArgumentException($"Cannot serialize object of type [{obj.GetType()}] in [{GetType()}]");
            }
        }
    }
}
