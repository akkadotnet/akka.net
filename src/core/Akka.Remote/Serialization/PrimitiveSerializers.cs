//-----------------------------------------------------------------------
// <copyright file="PrimitiveSerializers.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Text;
using Akka.Actor;
using Akka.Serialization;
using Akka.Util;

namespace Akka.Remote.Serialization
{
    public sealed class PrimitiveSerializers : Serializer
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PrimitiveSerializers" /> class.
        /// </summary>
        /// <param name="system">The actor system to associate with this serializer. </param>
        public PrimitiveSerializers(ExtendedActorSystem system) : base(system)
        {
        }

        /// <inheritdoc />
        public override bool IncludeManifest { get; } = true;

        /// <inheritdoc />
        public override byte[] ToBinary(object obj)
        {
            var str = obj as string;
            if (str != null) return Encoding.UTF8.GetBytes(str);
            if (obj is int) return BitConverter.GetBytes((int)obj);
            if (obj is long) return BitConverter.GetBytes((long)obj);

            throw new ArgumentException($"Cannot serialize object of type [{obj.GetType().TypeQualifiedName()}]");
        }

        /// <inheritdoc />
        public override object FromBinary(byte[] bytes, Type type)
        {
            if (type == typeof(string)) return Encoding.UTF8.GetString(bytes);
            if (type == typeof(int)) return BitConverter.ToInt32(bytes, 0);
            if (type == typeof(long)) return BitConverter.ToInt64(bytes, 0);

            throw new ArgumentException($"Unimplemented deserialization of message with manifest [{type.TypeQualifiedName()}] in [${nameof(PrimitiveSerializers)}]");
        }
    }
}
