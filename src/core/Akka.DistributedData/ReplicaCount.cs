using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.DistributedData
{
    public class GetReplicaCount
    {
        static readonly GetReplicaCount _instance = new GetReplicaCount();

        static GetReplicaCount Instance
        {
            get { return _instance; }
        }
    }

    public class ReplicaCount
    {
        readonly int _n;

        public int N
        {
            get { return _n; }
        }

        public ReplicaCount(int n)
        {
            _n = n;
        }

        public override bool Equals(object obj)
        {
            var other = obj as ReplicaCount;
            if(other != null)
            {
                return _n == other._n;
            }
            return false;
        }
    }
}
