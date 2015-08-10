using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.DistributedData
{
    internal class GetKeysIds
    {
    }

    internal sealed class GetKeysIdsResult
    {
        private IImmutableSet<string> _keys;

        internal IImmutableSet<string> Keys
        {
            get { return _keys; }
        }

        internal GetKeysIdsResult(IImmutableSet<string> keys)
        {

        }
    }
}
