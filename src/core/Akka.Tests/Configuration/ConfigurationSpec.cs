using Akka.Configuration.Hocon;
using System.Configuration;
using Xunit;

namespace Akka.Tests.Configuration
{
    public class ConfigurationSpec : AkkaSpec
    {
        [Fact]
        public void DeserializesHoconConfigurationFromNetConfigFile()
        {
            var section = (AkkaConfigurationSection)ConfigurationManager.GetSection("akka");
            Assert.NotNull(section);
            Assert.False(string.IsNullOrEmpty(section.Hocon.Content));
            var akkaConfig = section.AkkaConfig;
            Assert.NotNull(akkaConfig);
        }
    }
}