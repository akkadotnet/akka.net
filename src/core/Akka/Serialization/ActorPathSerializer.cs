using System;
using System.Text;
using Akka.Actor;

namespace Akka.Serialization
{
    public class ActorPathSerializer : Serializer
    {
        public ActorPathSerializer(ExtendedActorSystem system) : base(system)
        {
        }

        public override int Identifier
        {
            get { return -4;}
        }

        public override bool IncludeManifest
        {
            get { return false; }
        }

        public override byte[] ToBinary(object obj)
        {
            if (obj == null) return null;
            ActorPath path;
            if ((path = obj as ActorPath) != null)
            {
                var serialized = path.ToSerializationFormat();
                return Encoding.UTF8.GetBytes(serialized);
            }

            throw new NotSupportedException();
        }

        public override object FromBinary(byte[] bytes, Type type)
        {
            if (typeof (ActorPath).IsAssignableFrom(type))
            {
                var serialized = Encoding.UTF8.GetString(bytes);
                ActorPath path;
                if (ActorPath.TryParse(serialized, out path)) 
                    return path;
                
                throw new FormatException("ActorPathSerializer couldn't deserialize provided string into ActorPath: " + serialized);
            }

            throw new NotSupportedException();
        }
    }
}