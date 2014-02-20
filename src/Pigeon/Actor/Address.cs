using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Actor
{
    public class Address
    {
        public static readonly Address AllSystems = new Address("akka", "all-systems");

        public Address(string protocol,string system, string host=null, int? port=null)
        {
            this.Protocol = protocol;
            this.System = system;
            this.Host = host;
            this.Port = port;
            toString = new Lazy<string>(() =>
            {
                var sb = new StringBuilder();
                sb.AppendFormat("{0}://{1}", this.Protocol, this.System);
                if (!string.IsNullOrWhiteSpace(Host))
                    sb.AppendFormat("@{0}", Host);
                if (Port.HasValue)
                    sb.AppendFormat(":{0}", Port.Value);

                return sb.ToString();
            }, true);
        }

        private Lazy<string> toString;
        public string Host { get; private set; }
        public int? Port { get; private set; }
        public string System { get; private set; }
        public string Protocol { get; private set; }

        public override string ToString()
        {
            return toString.Value;
        }

        public override int GetHashCode()
        {
            return ToString().GetHashCode();
        }

        //TODO: implement real equals checks instead
        public override bool Equals(object obj)
        {
            if (obj == null)
                return false;

            return this.ToString() == obj.ToString();
        }
    }
}
