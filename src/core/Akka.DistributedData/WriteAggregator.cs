using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.DistributedData
{
    public class WriteAggregator : ReadWriteAggregator
    {
        protected override TimeSpan Timeout
        {
            get { throw new NotImplementedException(); }
        }

        protected override int DoneWhenRemainingSize
        {
            get { throw new NotImplementedException(); }
        }

        protected override bool Receive(object message)
        {
            throw new NotImplementedException();
        }
    }
}
