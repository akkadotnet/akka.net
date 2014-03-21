using Akka.Actor;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;

namespace Akka.Tests.Actor
{
    [TestClass]
    public class ActorSystemSpec : AkkaSpec
    {
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
    }
}