using System;
using Akka.Actor;
using System.Runtime.Serialization;

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

        protected ConfigurationException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}