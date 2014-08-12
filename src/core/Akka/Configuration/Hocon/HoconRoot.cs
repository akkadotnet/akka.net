using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Configuration.Hocon
{
    public class HoconRoot
    {
        public HoconRoot(HoconValue value, IEnumerable<HoconSubstitution> substitutions)
        {
            Value = value;
            Substitutions = Substitutions;
        }

        public HoconRoot(HoconValue value)
        {            
            this.Value = value;
            this.Substitutions = Enumerable.Empty<HoconSubstitution>();
        }
        public HoconValue Value { get;private set; }
        public IEnumerable<HoconSubstitution> Substitutions { get;private set; }
    }
}
