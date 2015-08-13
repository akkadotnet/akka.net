using Akka.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Serialization
{
    public abstract class SerializerWithStringManifest : Serializer
    {
        public abstract string Manifest(Object o);

        public sealed override bool IncludeManifest
        {
            get { return true; }
        }

        public abstract Object FromBinary(byte[] bytes, string manifest);

        public sealed override object FromBinary(byte[] bytes, Type type)
        {
            var manifestString = type == null ? "" : type.Name;
            return FromBinary(bytes, manifestString);
        }

        public SerializerWithStringManifest(ExtendedActorSystem system)
            : base(system)
        { }
    }
}
