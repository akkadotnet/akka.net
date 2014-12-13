using System;
using Akka.Actor;

namespace Akka.Configuration
{
    public class ConfigurationException : AkkaException
    {
        public ConfigurationException(string message) : base(message)
        {
        }

        public ConfigurationException(string message, Exception exception): base(message, exception)
        {
            
        }
    }
}