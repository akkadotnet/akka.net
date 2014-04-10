using Akka.Tests;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Akka.Remote.Tests
{
    [TestClass]
    public class RemoteConfigSpec : AkkaSpec
    {

        #region Setup / Configuration
        protected override string GetConfig()
        {
            return @"
                akka.actor.provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
                akka.remote.tcp-transport.port = 0
            ";
        }
        #endregion

        [TestMethod]
        public void Remoting_should_contain_correct_configuration_values_in_ReferenceConf()
        {
            var remoteSettings = ((RemoteActorRefProvider) sys.Provider).RemoteSettings;

            Assert.IsFalse(remoteSettings.LogReceive);
            Assert.IsFalse(remoteSettings.LogSend);
            Assert.IsFalse(remoteSettings.UntrustedMode);
            Assert.AreEqual(0, remoteSettings.TrustedSelectionPaths.Count);
        }
    }
}
