using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Akka.Tests
{
    public class AtomicInteger
    {
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
    }
}
