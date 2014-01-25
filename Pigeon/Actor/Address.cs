using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Actor
{
    public class Address
    {
        public Address(string protocol,ActorSystem system, string host=null, int? port=null)
        {
            this.Protocol = protocol;
            this.System = system;
            this.Host = host;
            this.Port = port;
            toString = new Lazy<string>(() =>
            {
                var sb = new StringBuilder();
                sb.AppendFormat("{0}://{1}", this.Protocol, this.System.Name);
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
        public ActorSystem System { get; private set; }
        public string Protocol { get; private set; }

  // override lazy val toString: String = {
  //  val sb = (new java.lang.StringBuilder(protocol)).append("://").append(system)

  //  if (host.isDefined) sb.append('@').append(host.get)
  //  if (port.isDefined) sb.append(':').append(port.get)

  //  sb.toString
  //}

        public override string ToString()
        {
            return toString.Value;
        }    
    }
}
