using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Configuration.Hocon
{
    public class HoconSubstitution
    {
        public string Path { get;private set; }

        public HoconSubstitution(string path)
        {
            this.Path = path;
        }
    }
}
