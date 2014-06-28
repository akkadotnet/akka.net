using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Akka
{
    public class AtomicBoolean
    {
        public AtomicBoolean()
        {
        }

        private bool value = false;
        public bool Value
        {
            get
            {
                return value;
            }
        }
        public bool Get()
        {            
            return value;
        }

        public bool GetAndSet(bool value)
        {
            this.value = value;
            return value;
        }
    }
}
