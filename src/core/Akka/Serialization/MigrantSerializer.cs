using System;
using System.IO;
using System.Security.Policy;
using Akka.Actor;
using Akka.Util;

namespace Akka.Serialization
{
    public class TypeSurrogate
    {
        public TypeSurrogate(string typeName)
        {
            TypeName = typeName;
        }
        public string TypeName { get;private set; }

        public Type Restore()
        {
            return Type.GetType(TypeName);
        }
    }
    public class MigrantSerializer : Serializer
    {
        private readonly Antmicro.Migrant.Serializer _serializer;
              public MigrantSerializer(ExtendedActorSystem system) : base(system)
        {
            _serializer = new Antmicro.Migrant.Serializer();

            _serializer.ForObject<ISurrogated>().SetSurrogate(x => 
                x.ToSurrogate(system));

            _serializer.ForSurrogate<ISurrogate>().SetObject(x => 
                x.FromSurrogate(system));

            _serializer.ForObject<Type>().SetSurrogate(t => 
                new TypeSurrogate(t.AssemblyQualifiedName));
            _serializer.ForSurrogate<TypeSurrogate>().SetObject(t => 
                t.Restore());
        }
        

        public override int Identifier
        {
            get { return -4; }
        }

        public override bool IncludeManifest
        {
            get { return false; }
        }

        public override byte[] ToBinary(object obj)
        {
            using (var stream = new MemoryStream())
            {
                _serializer.Serialize(obj,stream);
                stream.Position = 0;
                return stream.ToArray();
            }
        }

        public override object FromBinary(byte[] bytes, Type type)
        {
            using (var stream = new MemoryStream())
            {
                stream.Write(bytes,0,bytes.Length);
                stream.Position = 0;
                return _serializer.Deserialize<object>(stream);
            }
        }
    }
}
