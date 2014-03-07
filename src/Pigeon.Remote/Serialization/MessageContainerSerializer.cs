using Akka.Actor;
using Akka.Serialization;
using Google.ProtocolBuffers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Remote.Serialization
{
    public class MessageContainerSerializer : Serializer
    {

        public MessageContainerSerializer(ActorSystem system) : base(system) { }

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
            if(!(obj is ActorSelectionMessage))
            {
                throw new ArgumentException("Object must be of type ActorSelectionMessage");
            }

            return SerializeActorSelectionMessage((ActorSelectionMessage)obj);
        }

        private ByteString Serialize(object obj)
        {
            var serializer = this.system.Serialization.FindSerializerFor(obj);
            var bytes = serializer.ToBinary(obj);
            return ByteString.CopyFrom(bytes);
        }

        private object Deserialize(ByteString bytes, Type type)
        {
            var serializer = this.system.Serialization.FindSerializerForType(type);
            var o = serializer.FromBinary(bytes.ToByteArray(), type);
            return o;
        }

        private Selection.Builder BuildPattern(string matcher, PatternType tpe)
        {
            var builder = Selection.CreateBuilder().SetType(tpe);
            if (matcher != null)
                builder.SetMatcher(matcher);

            return builder;
        }
        private byte[] SerializeActorSelectionMessage(ActorSelectionMessage sel)
        {
            var builder = SelectionEnvelope.CreateBuilder();
            var serializer = system.Serialization.FindSerializerFor(sel.Message);
            builder.SetEnclosedMessage(ByteString.CopyFrom(serializer.ToBinary(sel.Message)));
            builder.SetSerializerId(serializer.Identifier);
            if (serializer.IncludeManifest)
            {
                builder.SetMessageManifest(ByteString.CopyFromUtf8(sel.Message.GetType().AssemblyQualifiedName));
            }
            foreach (var element in sel.Elements)
            {
                element.Match()
                    .With<SelectChildName>(m => builder.AddPattern(BuildPattern(m.Name, PatternType.CHILD_NAME)))
                    .With<SelectChildPattern>(m => builder.AddPattern(BuildPattern(m.PatternStr, PatternType.CHILD_PATTERN)))
                    .With<SelectParent>(m => builder.AddPattern(BuildPattern(null, PatternType.PARENT)));                    
            }

            return builder.Build().ToByteArray();
        }

        public override object FromBinary(byte[] bytes, Type type)
        {
            var selectionEnvelope = SelectionEnvelope.ParseFrom(bytes);
            Type msgType = null;
            if (selectionEnvelope.HasMessageManifest)
            {
                msgType = Type.GetType(selectionEnvelope.MessageManifest.ToStringUtf8());
            }
            var msg = Deserialize(selectionEnvelope.EnclosedMessage, msgType);
            var elements = selectionEnvelope.PatternList.Select<Selection, SelectionPathElement>(p =>
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
