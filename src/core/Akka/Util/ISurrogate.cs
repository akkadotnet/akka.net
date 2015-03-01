using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;

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
}
