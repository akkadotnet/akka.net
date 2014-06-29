using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Akka.Actor;
using Akka.Dispatch;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Akka.Tests.Actor
{
    [TestClass]
    public class RootGuardianActorRef_Tests
    {
        static RootActorPath _rootActorPath = new RootActorPath(new Address("akka", "test"));
        static DummyActorRef _deadLetters = new DummyActorRef(_rootActorPath / "deadLetters");
        ReadOnlyDictionary<string, InternalActorRef> _emptyExtraNames = new ReadOnlyDictionary<string, InternalActorRef>(new Dictionary<string, InternalActorRef>());

        [TestMethod]
        public void Path_Should_be_the_same_path_as_specified()
        {
            var rootGuardianActorRef = new RootGuardianActorRef(_rootActorPath, null, _deadLetters, _emptyExtraNames);
            Assert.AreEqual(_rootActorPath, rootGuardianActorRef.Path);
        }

        [TestMethod]
        public void Parent_Should_be_itself()
        {
            var rootGuardianActorRef = new RootGuardianActorRef(_rootActorPath, null, _deadLetters, _emptyExtraNames);
            var parent = rootGuardianActorRef.Parent;
            Assert.AreSame(rootGuardianActorRef, parent);
        }


        [TestMethod]
        public void Getting_temp_child_Should_return_tempContainer()
        {
            var rootGuardianActorRef = new RootGuardianActorRef(_rootActorPath, null, _deadLetters, _emptyExtraNames);
            var tempContainer = new DummyActorRef(_rootActorPath / "temp");
            rootGuardianActorRef.SetTempContainer(tempContainer);
            var actorRef = rootGuardianActorRef.GetSingleChild("temp");
            Assert.AreSame(tempContainer, actorRef);
        }

        [TestMethod]
        public void Getting_deadLetters_child_Should_return_tempContainer()
        {
            var rootGuardianActorRef = new RootGuardianActorRef(_rootActorPath, null, _deadLetters, _emptyExtraNames);
            var actorRef = rootGuardianActorRef.GetSingleChild("deadLetters");
            Assert.AreSame(_deadLetters, actorRef);
        }


        [TestMethod]
        public void Getting_a_child_that_exists_in_extraNames_Should_return_the_child()
        {
            var extraNameChild = new DummyActorRef(_rootActorPath / "extra");
            var extraNames = new Dictionary<string, InternalActorRef> { { "extra", extraNameChild } };
            var rootGuardianActorRef = new RootGuardianActorRef(_rootActorPath,null, _deadLetters, extraNames);
            var actorRef = rootGuardianActorRef.GetSingleChild("extra");
            Assert.AreSame(extraNameChild, actorRef);
        }

        [Ignore]
        [TestMethod]
        public void Getting_an_unknown_child_that_exists_in_extraNames_Should_return_nobody()
        {
            var rootGuardianActorRef = new RootGuardianActorRef(_rootActorPath, null, _deadLetters, _emptyExtraNames);
            var actorRef = rootGuardianActorRef.GetSingleChild("unknown-child");
            Assert.AreSame(ActorRef.Nobody, actorRef);
        }


        private class DummyActorRef : MinimalActorRef
        {
            private readonly ActorPath _path;

            public DummyActorRef(ActorPath path)
            {
                _path = path;
            }

            public override ActorPath Path
            {
                get { return _path; }
            }

            public override ActorRefProvider Provider
            {
                get { throw new System.NotImplementedException(); }
            }
        }

    
    }
}