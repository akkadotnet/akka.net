using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Actor
{
    public class Address
    {
        public Address(string host, int port)
        {
            this.Host = host;
            this.Port = port;
        }

        public string Host { get;private set; }
        public int Port { get;private set; }
    }
}
