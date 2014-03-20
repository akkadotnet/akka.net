using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Tests
{
    public class Comparable
    {
        public override bool Equals(object obj)
        {
            if (obj == null)
                return false;

            return this.ToString() == obj.ToString();
        }
        public override int GetHashCode()
        {
            return this.ToString().GetHashCode();
        }

        public override string ToString()
        {
            var res = fastJSON.JSON.Instance.ToJSON(this);
            return res;
        }
    }
}
