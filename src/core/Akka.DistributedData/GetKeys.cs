using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.DistributedData
{
    internal class GetKeyIds
    {
        static readonly GetKeyIds _instance;
        public static GetKeyIds Instance
        {
            get { return _instance; }
        }

        private GetKeyIds()
        { }

        public override bool Equals(object obj)
        {
            return obj != null && obj is GetKeyIds;
        }
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
            _keys = keys;
        }
    }
}
