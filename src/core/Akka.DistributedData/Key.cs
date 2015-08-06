using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.DistributedData
{
    public abstract class Key<T> where T : IReplicatedData
    {
        private readonly string _id;

        public string Id
        {
            get { return _id; }
        }

        public Key(string id)
        {
            _id = id;
        }

        public sealed override bool Equals(object obj)
        {
            var other = obj as Key<T>;
            if(other != null)
            {
                return other.Id == Id;
            }
            else
            {
                return false;
            }
        }

        public override int GetHashCode()
        {
            return _id.GetHashCode();
        }

        public override string ToString()
        {
            return _id;
        }
    }
}
