using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Akka.Util
{
    public interface ISurrogate
    {
        ISurrogated FromSurrogate(ActorSystem system);
    }

    public interface ISurrogated
    {
        ISurrogate ToSurrogate(ActorSystem system);
    }

    public  class PrimitiveSurrogate
    {
        public string V { get; set; }
        public long T { get; set; }

        public PrimitiveSurrogate()
        {
            
        } 

        public PrimitiveSurrogate(int value)
        {
            V = value.ToString(NumberFormatInfo.InvariantInfo);
            T = 1L;
        }
        public PrimitiveSurrogate(float value)
        {
            V = value.ToString(NumberFormatInfo.InvariantInfo);
            T = 2L;
        }
        public PrimitiveSurrogate(decimal value)
        {
            V = value.ToString(NumberFormatInfo.InvariantInfo);
            T = 3L;
        }
        public object GetValue()
        {
            if (T == 1L)
                return int.Parse(V, NumberFormatInfo.InvariantInfo);
            if (T == 2L)
                return float.Parse(V, NumberFormatInfo.InvariantInfo);
            if (T == 3L)
                return decimal.Parse(V, NumberFormatInfo.InvariantInfo);

            throw new NotSupportedException();
        }
    }  
}
