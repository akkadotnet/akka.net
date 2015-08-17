using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.DistributedData
{
    internal interface ICommand
    {
        IKey Key { get; }
    }

    internal interface ICommand<T> where T : IReplicatedData
    {
        IKey<T> Key { get; }
    }

    public abstract class BaseCommand : ICommand, IReplicatorMessage
    {
        readonly IKey _key;
        IKey ICommand.Key { get { return _key; } }

        internal BaseCommand(IKey key)
        {
            _key = key;
        }
    }
}
