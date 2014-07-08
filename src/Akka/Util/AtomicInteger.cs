using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Akka.Util
{
    public class AtomicInteger
    {
        public AtomicInteger(int seed = -1)
        {
            value = seed;
        }

        private int value = -1;
        public int Value
        {
            get
            {
                return value;
            }
        }
        public int GetAndIncrement()
        {
            Interlocked.Increment(ref value);
            return value;
        }

        public int GetAndAdd(int amount)
        {
            Interlocked.Add(ref value, amount);
            return value;
        }

        public void Set(int newValue)
        {
            Interlocked.Exchange(ref this.value, newValue);
        }
    }
}
