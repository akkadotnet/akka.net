using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.DistributedData
{
    public class DeletedData : AbstractReplicatedData<DeletedData>
    {
        public override DeletedData Merge(DeletedData other)
        {
            return this;
        }

        public override bool Equals(object obj)
        {
            return obj is DeletedData;
        }
    }
}
