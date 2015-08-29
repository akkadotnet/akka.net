using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.DistributedData
{
    internal interface ICommand<T> where T : IReplicatedData
    {
        IKey<T> Key { get; }
    }
}
