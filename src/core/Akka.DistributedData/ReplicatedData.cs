using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.DistributedData
{
    public interface IReplicatedData
    {
        object Merge(object other);
    }

    public interface IReplicatedData<T> : IReplicatedData
    {
        T Merge(T other);
    }

    public abstract class AbstractReplicatedData<T> : IReplicatedData<T>
    {
        public abstract T Merge(T other);

        public object Merge(object other)
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
