using Akka.Actor;

namespace Akka.Configuration
{
    public class ConfigurationException : AkkaException
    {
        public ConfigurationException(string message) : base(message)
        {
        }
    }
}