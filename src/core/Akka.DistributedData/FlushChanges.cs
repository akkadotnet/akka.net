using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.DistributedData
{
    public class FlushChanges
    {
        static readonly FlushChanges _instance;

        public static FlushChanges Instance
        {
            get { return _instance; }
        }

        private FlushChanges()
        { }

        public override bool Equals(object obj)
        {
            return obj != null && obj is FlushChanges;
        }
    }
}
