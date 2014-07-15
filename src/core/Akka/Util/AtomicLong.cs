using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Akka.Util
{
    public class AtomicLong
    {
        public AtomicLong(long seed = -1)
        {
            value = seed;
        }

        private long value = -1;
        public long Value
        {
            get
            {
                return value;
            }
        }
        public long GetAndIncrement()
        {
            Interlocked.Increment(ref value);
            return value;
        }

        public long GetAndAdd(int amount)
        {
            Interlocked.Add(ref value, amount);
            return value;
        }

        public void Set(long newValue)
        {
            Interlocked.Exchange(ref this.value, newValue);
        }
    }
}
