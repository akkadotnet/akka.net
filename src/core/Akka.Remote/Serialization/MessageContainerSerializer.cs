//-----------------------------------------------------------------------
// <copyright file="MessageContainerSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;
using Akka.Serialization;
using Akka.Util;
using Google.Protobuf;

namespace Akka.Remote.Serialization
{
    /// <summary>
    /// This is a special <see cref="Serializer"/> that serializes and deserializes <see cref="ActorSelectionMessage"/> only.
    /// </summary>
    public class MessageContainerSerializer : Serializer
    {
        private readonly WrappedPayloadSupport _payloadSupport;

        /// <summary>
        /// Initializes a new instance of the <see cref="MessageContainerSerializer"/> class.
        /// </summary>
        /// <param name="system">The actor system to associate with this serializer. </param>
        public MessageContainerSerializer(ExtendedActorSystem system) : base(system)
        {
            _payloadSupport = new WrappedPayloadSupport(system);
        }

        /// <inheritdoc />
        public override bool IncludeManifest => false;

        /// <inheritdoc />
        public override byte[] ToBinary(object obj)
        {
            if (obj is ActorSelectionMessage sel)
            {
                var envelope = new Proto.Msg.SelectionEnvelope();
                envelope.Payload = _payloadSupport.PayloadToProto(sel.Message);

                foreach (var element in sel.Elements)
                {
                    Proto.Msg.Selection selection = null;
                    if (element is SelectChildName m1)
                    {
                        selection = BuildPattern(m1.Name, Proto.Msg.Selection.Types.PatternType.ChildName);
                    }
                    else if (element is SelectChildPattern m)
                    {
                        selection = BuildPattern(m.PatternStr, Proto.Msg.Selection.Types.PatternType.ChildPattern);
                    }
                    else if (element is SelectParent)
                    {
                        selection = BuildPattern(null, Proto.Msg.Selection.Types.PatternType.Parent);
                    }

                    envelope.Pattern.Add(selection);
                }
                

                return envelope.ToByteArray();
            }

            throw new ArgumentException($"Cannot serialize object of type [{obj.GetType().TypeQualifiedName()}]");
        }

        /// <inheritdoc />
        public override object FromBinary(byte[] bytes, Type type)
        {
            var selectionEnvelope = Proto.Msg.SelectionEnvelope.Parser.ParseFrom(bytes);
            var message = _payloadSupport.PayloadFrom(selectionEnvelope.Payload);

            var elements = new SelectionPathElement[selectionEnvelope.Pattern.Count];
            for (var i = 0; i < selectionEnvelope.Pattern.Count; i++)
            {
                var p = selectionEnvelope.Pattern[i];
                if (p.Type == Proto.Msg.Selection.Types.PatternType.ChildName)
                    elements[i] = new SelectChildName(p.Matcher);
                if (p.Type == Proto.Msg.Selection.Types.PatternType.ChildPattern)
                    elements[i] = new SelectChildPattern(p.Matcher);
                if (p.Type == Proto.Msg.Selection.Types.PatternType.Parent)
                    elements[i] = new SelectParent();
            }

            return new ActorSelectionMessage(message, elements);
        }

        private Proto.Msg.Selection BuildPattern(string matcher, Proto.Msg.Selection.Types.PatternType tpe)
        {
            var selection = new Proto.Msg.Selection { Type = tpe };
            if (matcher != null)
                selection.Matcher = matcher;

            return selection;
        }
    }
}
