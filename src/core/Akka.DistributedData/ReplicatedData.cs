using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.DistributedData
{
    public interface IReplicatedData
    {
        IReplicatedData Merge(IReplicatedData other);
    }

    public interface IReplicatedData<T> : IReplicatedData where T : IReplicatedData
    {
        T Merge(T other);
    }

    public abstract class AbstractReplicatedData<T> : IReplicatedData<T> where T : IReplicatedData
    {
        public abstract T Merge(T other);

        public IReplicatedData Merge(IReplicatedData other)
        {
            if(other is T)
            {
                return Merge((T)other);
            }
            else
            {
                throw new ArgumentException("Unable to merge different CRDTs");
            }
        }
    }

}
