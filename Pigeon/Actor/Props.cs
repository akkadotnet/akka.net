using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Actor
{
    public class Props
    {
        public Props(Type type)
        {
            this.Type = type;
        }

        public Type Type { get;private set; }
    }
}
