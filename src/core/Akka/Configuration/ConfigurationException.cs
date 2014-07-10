using Akka.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Configuration
{
    public class ConfigurationException : AkkaException
    {
        public ConfigurationException(string message) : base(message) { }
    }
}
