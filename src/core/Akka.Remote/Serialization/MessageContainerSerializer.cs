//-----------------------------------------------------------------------
// <copyright file="MessageContainerSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;
using Akka.Remote.Proto;
using Akka.Serialization;
using Google.Protobuf;

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
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified <paramref name="obj"/> is not of type <see cref="ActorSelectionMessage"/>.
        /// </exception>
        public override byte[] ToBinary(object obj)
        {
            if (!(obj is ActorSelectionMessage))
            {
                throw new ArgumentException("Object must be of type ActorSelectionMessage", nameof(obj));
            }

            return SerializeSelection((ActorSelectionMessage)obj);
        }

        private object Deserialize(ByteString bytes, Type type, int serializerId)
        {
            Serializer serializer = system.Serialization.GetSerializerById(serializerId);
            object o = serializer.FromBinary(bytes.ToByteArray(), type);
            return o;
        }

        private Selection BuildPattern(string matcher, PatternType tpe)
        {
            var selection = new Selection { Type = tpe };
            if (matcher != null)
                selection.Matcher = matcher;

            return selection;
        }

        private byte[] SerializeSelection(ActorSelectionMessage sel)
        {
            var message = sel.Message;
            Serializer serializer = system.Serialization.FindSerializerFor(message);

            var envelope = new SelectionEnvelope
            {
                EnclosedMessage = ByteString.CopyFrom(serializer.ToBinary(message)),
                SerializerId = serializer.Identifier
            };
            var serializer2 = serializer as SerializerWithStringManifest;
            if (serializer2 != null)
            {
                var manifest = serializer2.Manifest(message);
                if (!string.IsNullOrEmpty(manifest))
                {
                    envelope.MessageManifest = ByteString.CopyFromUtf8(manifest);
                }
            }
            else
            {
                if (serializer.IncludeManifest)
                    envelope.MessageManifest = ByteString.CopyFromUtf8(message.GetType().AssemblyQualifiedName);
            }

            foreach (SelectionPathElement element in sel.Elements)
            {
                var selection = element.Match<Selection>()
                    .With<SelectChildName>(m => BuildPattern(m.Name, PatternType.ChildName))
                    .With<SelectChildPattern>(
                        m => BuildPattern(m.PatternStr, PatternType.ChildPattern))
                    .With<SelectParent>(m => BuildPattern(null, PatternType.Parent))
                    .ResultOrDefault(_ => null);
                envelope.Pattern.Add(selection);
            }

            return envelope.ToByteArray();
        }

        /// <summary>
        /// Deserializes a byte array into an object of type <paramref name="type"/>.
        /// </summary>
        /// <param name="bytes">The array containing the serialized object</param>
        /// <param name="type">The type of object contained in the array</param>
        /// <returns>The object contained in the array</returns>
        /// <exception cref="NotSupportedException">
        /// This exception is thrown if the <see cref="SelectionEnvelope"/>, contained within the specified byte array
        /// <paramref name="bytes"/>, contains an unknown <see cref="PatternType"/>.
        /// </exception>
        public override object FromBinary(byte[] bytes, Type type)
        {
            SelectionEnvelope selectionEnvelope = SelectionEnvelope.Parser.ParseFrom(bytes);
            var manifest = !selectionEnvelope.MessageManifest.IsEmpty ? selectionEnvelope.MessageManifest.ToStringUtf8() : string.Empty;
            //TODO the line above must be the same as selectionEnvelope.MessageManifest.ToStringUtf8()
            var message = system.Serialization.Deserialize(
                selectionEnvelope.EnclosedMessage.ToByteArray(),
                selectionEnvelope.SerializerId,
                manifest);

            var elements = selectionEnvelope.Pattern.Select<Selection, SelectionPathElement>(p =>
            {
                if (p.Type == PatternType.Parent)
                    return new SelectParent();
                if (p.Type == PatternType.ChildName)
                    return new SelectChildName(p.Matcher);
                if (p.Type == PatternType.ChildPattern)
                    return new SelectChildPattern(p.Matcher);
                throw new NotSupportedException("Unknown SelectionEnvelope.Elements.Type");
            }).ToArray();

            return new ActorSelectionMessage(message, elements);
        }
    }
}