using Akka.Configuration.Hocon;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Configuration;

namespace Akka.Tests.Configuration
{
    [TestClass]
    public class ConfigurationSpec : AkkaSpec
    {
        [TestMethod]
        public void DeserializesHoconConfigurationFromNetConfigFile()
        {
            var section = (AkkaConfigurationSection)ConfigurationManager.GetSection("akka");
            Assert.IsNotNull(section);
            Assert.IsFalse(string.IsNullOrEmpty(section.Hocon.Content));
            var akkaConfig = section.AkkaConfig;
            Assert.IsNotNull(akkaConfig);
        }
    }
}