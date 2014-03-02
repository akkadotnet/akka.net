using Akka.Actor;
using Akka.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Remote.Serialization
{
    public class DaemonMsgCreateSerializer : Serializer
    {
        public DaemonMsgCreateSerializer(ActorSystem system) : base(system) { }

        public override int Identifier
        {
            get { return 3; }
        }

        public override bool IncludeManifest
        {
            get { return false; }
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
