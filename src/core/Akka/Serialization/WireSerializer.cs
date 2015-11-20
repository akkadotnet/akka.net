//-----------------------------------------------------------------------
// <copyright file="WireSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Util;
using Wire;

namespace Akka.Serialization
{
    public class WireSerializer : Serializer
    {
        private readonly Wire.Serializer _seralizer;

        public WireSerializer(ExtendedActorSystem system) : base(system)
        {
            var akkaSurrogate = Surrogate.Create<ISurrogated,ISurrogate>(from => from.ToSurrogate(system),to => to.FromSurrogate(system));
            _seralizer = new  Wire.Serializer(new SerializerOptions(preserveObjectReferences: true, versionTolerance:true,surrogates: new Surrogate[]{ akkaSurrogate }));
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
            using (var ms = new MemoryStream())
            {
                _seralizer.Serialize(obj,ms);
                return ms.ToArray();
            }
        }

        public override object FromBinary(byte[] bytes, Type type)
        {
            using (var ms = new MemoryStream())
            {
                ms.Write(bytes,0,bytes.Length);
                ms.Position = 0;
                var res = _seralizer.Deserialize<object>(ms);
                return res;
            }
        }
    }
}
