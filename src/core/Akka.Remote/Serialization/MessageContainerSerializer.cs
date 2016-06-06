//-----------------------------------------------------------------------
// <copyright file="MessageContainerSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;
using Akka.Serialization;
using Google.ProtocolBuffers;

namespace Akka.Remote.Serialization
{
    /// <summary>
    /// This is a special <see cref="Serializer"/> that serializes and deserializes <see cref="ActorSelectionMessage"/> only.
    /// </summary>
    public class MessageContainerSerializer : Serializer
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MessageContainerSerializer"/> class.
        /// </summary>
        /// <param name="system">The actor system to associate with this serializer. </param>
        public MessageContainerSerializer(ExtendedActorSystem system) : base(system)
        {
        }

        /// <summary>
        /// Completely unique value to identify this implementation of Serializer, used to optimize network traffic
        /// Values from 0 to 16 is reserved for Akka internal usage
        /// </summary>
        public override int Identifier
        {
            get { return 6; }
        }

        /// <summary>
        /// Returns whether this serializer needs a manifest in the fromBinary method
        /// </summary>
        public override bool IncludeManifest
        {
            get { return false; }
        }

        /// <summary>
        /// Serializes the given object into a byte array
        /// </summary>
        /// <param name="obj">The object to serialize </param>
        /// <returns>A byte array containing the serialized object</returns>
        /// <exception cref="ArgumentException">Object must be of type <see cref="ActorSelectionMessage"/></exception>
        public override byte[] ToBinary(object obj)
        {
            if (!(obj is ActorSelectionMessage))
            {
                throw new ArgumentException("Object must be of type ActorSelectionMessage");
            }

            return SerializeSelection((ActorSelectionMessage)obj);
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

        private byte[] SerializeSelection(ActorSelectionMessage sel)
        {
            SelectionEnvelope.Builder builder = SelectionEnvelope.CreateBuilder();
            var message = sel.Message;
            Serializer serializer = system.Serialization.FindSerializerFor(message);
            builder
                .SetEnclosedMessage(ByteString.CopyFrom(serializer.ToBinary(message)))
                .SetSerializerId(serializer.Identifier);

            var serializer2 = serializer as SerializerWithStringManifest;
            if (serializer2 != null)
            {
                var manifest = serializer2.Manifest(message);
                if (!string.IsNullOrEmpty(manifest))
                {
                    builder.SetMessageManifest(ByteString.CopyFromUtf8(manifest));
                }
            }
            else
            {
                if (serializer.IncludeManifest)
                    builder.SetMessageManifest(ByteString.CopyFromUtf8(message.GetType().AssemblyQualifiedName));
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

        /// <summary>
        /// Deserializes a byte array into an object of type <paramref name="type"/>.
        /// </summary>
        /// <param name="bytes">The array containing the serialized object</param>
        /// <param name="type">The type of object contained in the array</param>
        /// <returns>The object contained in the array</returns>
        /// <exception cref="NotSupportedException">Unknown SelectionEnvelope.Elements.Type</exception>
        public override object FromBinary(byte[] bytes, Type type)
        {
            SelectionEnvelope selectionEnvelope = SelectionEnvelope.ParseFrom(bytes);
            var manifest = selectionEnvelope.HasMessageManifest ? selectionEnvelope.MessageManifest.ToStringUtf8() : string.Empty;
            var message = system.Serialization.Deserialize(
                selectionEnvelope.EnclosedMessage.ToByteArray(),
                selectionEnvelope.SerializerId,
                manifest);

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

            return new ActorSelectionMessage(message, elements);
        }
    }
}