using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.DistributedData
{
    public sealed class Flag : AbstractReplicatedData<Flag>
    {
        private readonly bool _enabled;

        public bool Enabled
        {
            get { return _enabled; }
        }

        public Flag()
            : this(false)
        { }

        public Flag(bool enabled)
        {
            _enabled = enabled;
        }

        public override Flag Merge(Flag other)
        {
            if(other.Enabled)
            {
                return other;
            }
            else
            {
                return this;
            }
        }

        public Flag SwitchOn()
        {
            if(_enabled)
            {
                return this;
            }
            else
            {
                return new Flag(true);
            }
        }

        public override bool Equals(object obj)
        {
            var other = obj as Flag;
            if(other != null)
            {
                return Enabled == other.Enabled;
            }
            return false;
        }
    }

    public sealed class FlagKey : Key<Flag>
    {
        public FlagKey(string id)
            : base(id)
        { }
    }
}
