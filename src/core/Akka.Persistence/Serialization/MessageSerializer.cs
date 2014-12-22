using System;
using Akka.Actor;
using Akka.Serialization;

namespace Akka.Persistence.Serialization
{
    public interface IMessage { }

    public class MessageSerializer : Serializer
    {
        public MessageSerializer(ExtendedActorSystem system)
            : base(system)
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
            if (obj is IPersistentRepresentation) return PersistentToBinary(obj as IPersistentRepresentation);
            if (obj is GuaranteedDeliverySnapshot) return SnapshotToBinary(obj as GuaranteedDeliverySnapshot);

            throw new ArgumentException(typeof(MessageSerializer) + " cannot serialize object of type " + obj.GetType());
        }

        public override object FromBinary(byte[] bytes, Type type)
        {
            if (type == null || type == typeof(Persistent) || type == typeof(IPersistentRepresentation)) return PersistentMessageFrom(bytes);
            if (type == typeof (GuaranteedDeliverySnapshot)) return SnapshotFrom(bytes);

            throw new ArgumentException(typeof(MessageSerializer) + " cannot deserialize object of type " + type);
        }

        private GuaranteedDeliverySnapshot SnapshotFrom(byte[] bytes)
        {
            throw new NotImplementedException();
        }

        private IPersistentRepresentation PersistentMessageFrom(byte[] bytes)
        {
            throw new NotImplementedException();
        }

        private byte[] SnapshotToBinary(GuaranteedDeliverySnapshot guaranteedDeliverySnapshot)
        {
            throw new NotImplementedException();
        }

        private byte[] PersistentToBinary(IPersistentRepresentation persistentRepresentation)
        {
            throw new NotImplementedException();
        }
    }
}