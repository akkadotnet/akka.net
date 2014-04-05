using Akka.Actor;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;

namespace Akka.Tests.Actor
{
    [TestClass]
    public class ActorSystemSpec : AkkaSpec
    {
        private string config = @"akka.extensions = [""Akka.Tests.Actor.TestExtension,Akka.Tests""]";
        protected override string GetConfig()
        {
            return config;
        }

        [TestMethod]
        public void AnActorSystemMustRejectInvalidNames()
        {
            (new List<string> { 
                  "hallo_welt",
                  "-hallowelt",
                  "hallo*welt",
                  "hallo@welt",
                  "hallo#welt",
                  "hallo$welt",
                  "hallo%welt",
                  "hallo/welt"}).ForEach(n => intercept<ArgumentException>(() => ActorSystem.Create(n)));
        }

        [TestMethod]
        public void AnActorSystemMustAllowValidNames()
        {
            ActorSystem
                .Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-")
                .Shutdown();
        }

        #region Extensions tests

        

        [TestMethod]
        public void AnActorSystem_Must_Support_Extensions()
        {
            Assert.IsTrue(sys.HasExtension<TestExtensionImpl>());
            var testExtension = sys.WithExtension<TestExtensionImpl>();
            Assert.AreEqual(sys, testExtension.System);
        }

        [TestMethod]
        public void AnActorSystem_Must_Support_Dynamically_Regsitered_Extensions()
        {
            Assert.IsFalse(sys.HasExtension<OtherTestExtensionImpl>());
            var otherTestExtension = sys.WithExtension<OtherTestExtensionImpl>(typeof(OtherTestExtension));
            Assert.IsTrue(sys.HasExtension<OtherTestExtensionImpl>());
            Assert.AreEqual(sys, otherTestExtension.System);
        }

        #endregion
    }

    public class OtherTestExtension : ExtensionIdProvider<OtherTestExtensionImpl>
    {
        public override OtherTestExtensionImpl CreateExtension(ActorSystem system)
        {
            return new OtherTestExtensionImpl(system);
        }
    }

    public class OtherTestExtensionImpl : IExtension
    {
        public OtherTestExtensionImpl(ActorSystem system)
        {
            System = system;
        }

        public ActorSystem System { get; private set; }
    }

    public class TestExtension : ExtensionIdProvider<TestExtensionImpl>
    {
        public override TestExtensionImpl CreateExtension(ActorSystem system)
        {
            return new TestExtensionImpl(system);
        }
    }

    public class TestExtensionImpl : IExtension
    {
        public TestExtensionImpl(ActorSystem system)
        {
            System = system;
        }

        public ActorSystem System { get; private set; }
    }
}