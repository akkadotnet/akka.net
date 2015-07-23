//-----------------------------------------------------------------------
// <copyright file="MessageContainerSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;
using Akka.Serialization;
using Google.ProtocolBuffers;

namespace Akka.Remote.Serialization
{
    /*TODO: this class is not used*/public class MessageContainerSerializer : Serializer
    {
        public MessageContainerSerializer(ExtendedActorSystem system) : base(system)
        {
        }

        public override int Identifier
        {
            get { return 6; }
        }

        public override bool IncludeManifest
        {
            get { return false; }
        }

        public override byte[] ToBinary(object obj)
        {
            if (!(obj is ActorSelectionMessage))
            {
                throw new ArgumentException("Object must be of type ActorSelectionMessage");
            }

            return SerializeActorSelectionMessage((ActorSelectionMessage) obj);
        }

        private ByteString Serialize(object obj)
        {
            Serializer serializer = system.Serialization.FindSerializerFor(obj);
            byte[] bytes = serializer.ToBinary(obj);
            return ByteString.CopyFrom(bytes);
        }

        private object Deserialize(ByteString bytes, Type type, int serializerId)
        {
            Serializer serializer = system.Serialization.GetSerializerById(serializerId);
            object o = serializer.FromBinary(bytes.ToByteArray(), type);
            return o;
        }

        private Selection.Builder BuildPattern(string matcher, PatternType tpe)
        {
            Selection.Builder builder = Selection.CreateBuilder().SetType(tpe);
            if (matcher != null)
                builder.SetMatcher(matcher);

            return builder;
        }

        private byte[] SerializeActorSelectionMessage(ActorSelectionMessage sel)
        {
            SelectionEnvelope.Builder builder = SelectionEnvelope.CreateBuilder();
            Serializer serializer = system.Serialization.FindSerializerFor(sel.Message);
            builder.SetEnclosedMessage(ByteString.CopyFrom(serializer.ToBinary(sel.Message)));
            builder.SetSerializerId(serializer.Identifier);
            if (serializer.IncludeManifest)
            {
                builder.SetMessageManifest(ByteString.CopyFromUtf8(sel.Message.GetType().AssemblyQualifiedName));
            }
            foreach (SelectionPathElement element in sel.Elements)
            {
                element.Match()
                    .With<SelectChildName>(m => builder.AddPattern(BuildPattern(m.Name, PatternType.CHILD_NAME)))
                    .With<SelectChildPattern>(
                        m => builder.AddPattern(BuildPattern(m.PatternStr, PatternType.CHILD_PATTERN)))
                    .With<SelectParent>(m => builder.AddPattern(BuildPattern(null, PatternType.PARENT)));
            }

            return builder.Build().ToByteArray();
        }

        public override object FromBinary(byte[] bytes, Type type)
        {
            SelectionEnvelope selectionEnvelope = SelectionEnvelope.ParseFrom(bytes);
            Type msgType = null;
            if (selectionEnvelope.HasMessageManifest)
            {
                msgType = Type.GetType(selectionEnvelope.MessageManifest.ToStringUtf8());
            }
            int serializerId = selectionEnvelope.SerializerId;
            object msg = Deserialize(selectionEnvelope.EnclosedMessage, msgType, serializerId);
            SelectionPathElement[] elements = selectionEnvelope.PatternList.Select<Selection, SelectionPathElement>(p =>
            {
                if (p.Type == PatternType.PARENT)
                    return new SelectParent();
                if (p.Type == PatternType.CHILD_NAME)
                    return new SelectChildName(p.Matcher);
                if (p.Type == PatternType.CHILD_PATTERN)
                    return new SelectChildPattern(p.Matcher);
                throw new NotSupportedException("Unknown SelectionEnvelope.Elements.Type");
            }).ToArray();
            return new ActorSelectionMessage(msg, elements);
        }
    }
}

