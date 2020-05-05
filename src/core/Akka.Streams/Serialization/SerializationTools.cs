//-----------------------------------------------------------------------
// <copyright file="SerializationTools.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Streams.Implementation.StreamRef;
using Akka.Streams.Serialization.Proto.Msg;
using Akka.Util;

namespace Akka.Streams.Serialization
{
    internal static class SerializationTools
    {
        public static Type TypeFromString(string typeName) => Type.GetType(typeName, throwOnError: true);

        public static Type TypeFromProto(EventType eventType) => TypeFromString(eventType.TypeName);

        public static EventType TypeToProto(Type clrType) => new EventType
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
    }
}
