using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Util
{
    public interface ISurrogate
    {
        object Translate();
    }
}
