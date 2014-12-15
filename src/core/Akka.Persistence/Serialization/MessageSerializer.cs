using System;
using Akka.Actor;
using Akka.Serialization;

namespace Akka.Persistence.Serialization
{
    public interface IMessage { }

    public class MessageSerializer : Serializer
    {
        public MessageSerializer(ExtendedActorSystem system) : base(system)
        {
        }

        public override int Identifier
        {
            get { return 7; }
        }

        public override bool IncludeManifest
        {
            get { return true; }
        }

        public override byte[] ToBinary(object obj)
        {
            throw new NotImplementedException();
        }

        public override object FromBinary(byte[] bytes, Type type)
        {
            throw new NotImplementedException();
        }
    }
}