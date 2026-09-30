//-----------------------------------------------------------------------
// <copyright file="SerializationTools.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Streams.Implementation.StreamRef;
using Akka.Streams.Serialization.Proto.Msg;
using Akka.Util;

namespace Akka.Streams.Serialization
{
    internal static class SerializationTools
    {
        /// <summary>
        /// Resolves the element type carried on the wire by a stream ref. With
        /// <see cref="AkkaFeatures.IsDynamicTypeLoadingSupported"/> off, there is no built-in table of stream-ref
        /// element types to fall back on - stream refs are generic over any type the application chooses - so
        /// the only option is a clear failure instead of an unreliable runtime type load.
        /// </summary>
        public static Type TypeFromString(string typeName)
        {
            if (AkkaFeatures.IsDynamicTypeLoadingSupported)
                return ResolveTypeFromString(typeName);

            throw new SerializationException(StreamRefTypeNotSupported(typeName));
        }

        [RequiresUnreferencedCode("Resolves a stream-ref element type carried on the wire by name. The trimmer cannot tell which type that is, so it may have been trimmed away.")]
        private static Type ResolveTypeFromString(string typeName) => Type.GetType(typeName, throwOnError: true);

        /// <summary>
        /// The message every stream-ref site throws with <see cref="AkkaFeatures.IsDynamicTypeLoadingSupported"/>
        /// off, instead of <see cref="AkkaFeatures.NotBuiltIn"/> - there is no HOCON setting here, and no built-in
        /// table of stream-ref element types to point at, so that message reads oddly for this failure.
        /// </summary>
        internal static string StreamRefTypeNotSupported(string eventTypeName) =>
            $"Cannot deserialize a stream ref with element type [{eventTypeName}]: stream refs need Akka.DynamicTypeLoading enabled at publish time (see #8667).";

        public static Type TypeFromProto(EventType eventType) => TypeFromString(eventType.TypeName);

        public static EventType TypeToProto(Type clrType) => new()
        {
            TypeName = clrType.TypeQualifiedName()
        };

        public static SourceRef ToSourceRef(SourceRefImpl sourceRef)
        {
            return new SourceRef
            {
                EventType = TypeToProto(sourceRef.EventType),
                OriginRef = new ActorRef
                {
                    Path = Akka.Serialization.Serialization.SerializedActorPath(sourceRef.InitialPartnerRef)
                }
            };
        }

        public static ISurrogate ToSurrogate(SourceRefImpl sourceRef)
        {
            var srcRef = ToSourceRef(sourceRef);
            return new SourceRefSurrogate(srcRef.EventType.TypeName, srcRef.OriginRef.Path);
        }

        public static SourceRefImpl ToSourceRefImpl(ExtendedActorSystem system, string eventType, string originPath)
        {
            var type = TypeFromString(eventType);
            var originRef = system.Provider.ResolveActorRef(originPath);

            return SourceRefImpl.Create(type, originRef);
        }

        public static SinkRef ToSinkRef(SinkRefImpl sinkRef)
        {
            return new SinkRef()
            {
                EventType = TypeToProto(sinkRef.EventType),
                TargetRef = new ActorRef()
                {
                    Path = Akka.Serialization.Serialization.SerializedActorPath(sinkRef.InitialPartnerRef)
                }
            };
        }

        public static ISurrogate ToSurrogate(SinkRefImpl sinkRef)
        {
            var snkRef = ToSinkRef(sinkRef);
            return new SinkRefSurrogate(snkRef.EventType.TypeName, snkRef.TargetRef.Path);
        }

        public static SinkRefImpl ToSinkRefImpl(ExtendedActorSystem system, string eventType, string originPath)
        {
            var type = TypeFromString(eventType);
            var originRef = system.Provider.ResolveActorRef(originPath);

            return SinkRefImpl.Create(type, originRef);
        }
    }
}
