//-----------------------------------------------------------------------
// <copyright file="RootGuardianActorRef_Tests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Collections.ObjectModel;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Dispatch;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Actor
{
    
    public class RootGuardianActorRef_Tests : AkkaSpec
    {
        static RootActorPath _rootActorPath = new RootActorPath(new Address("akka", "test"));
        DummyActorRef _deadLetters = new DummyActorRef(_rootActorPath / "deadLetters");
        ReadOnlyDictionary<string, IInternalActorRef> _emptyExtraNames = new ReadOnlyDictionary<string, IInternalActorRef>(new Dictionary<string, IInternalActorRef>());
        MessageDispatcher _dispatcher;

        public RootGuardianActorRef_Tests()
        {
            _dispatcher = Sys.Dispatchers.Lookup(CallingThreadDispatcher.Id);
        }

        [Fact]
        public void Path_Should_be_the_same_path_as_specified()
        {
            var props = Props.Create<GuardianActor>(new OneForOneStrategy(e => Directive.Stop));
            var rootGuardianActorRef = new RootGuardianActorRef((ActorSystemImpl) Sys, props, _dispatcher, Sys.Mailboxes.GetMailboxType(props, _dispatcher.Configurator.Config), ActorRefs.Nobody, _rootActorPath, _deadLetters, _emptyExtraNames);
            Assert.Equal(_rootActorPath, rootGuardianActorRef.Path);
        }

        [Fact]
        public void Parent_Should_be_itself()
        {
            var props = Props.Create<GuardianActor>(new OneForOneStrategy(e => Directive.Stop));
            var rootGuardianActorRef = new RootGuardianActorRef((ActorSystemImpl) Sys, props, _dispatcher, Sys.Mailboxes.GetMailboxType(props, _dispatcher.Configurator.Config), ActorRefs.Nobody, _rootActorPath, _deadLetters, _emptyExtraNames);
            var parent = rootGuardianActorRef.Parent;
            Assert.Same(rootGuardianActorRef, parent);
        }


        [Fact]
        public void Getting_temp_child_Should_return_tempContainer()
        {
            var props = Props.Create<GuardianActor>(new OneForOneStrategy(e => Directive.Stop));
            var rootGuardianActorRef = new RootGuardianActorRef((ActorSystemImpl)Sys, props, _dispatcher, Sys.Mailboxes.GetMailboxType(props, _dispatcher.Configurator.Config), ActorRefs.Nobody, _rootActorPath, _deadLetters, _emptyExtraNames);
            var tempContainer = new DummyActorRef(_rootActorPath / "temp");
            rootGuardianActorRef.SetTempContainer(tempContainer);
            var actorRef = rootGuardianActorRef.GetSingleChild("temp");
            Assert.Same(tempContainer, actorRef);
        }

        [Fact]
        public void Getting_deadLetters_child_Should_return_tempContainer()
        {
            var props = Props.Create<GuardianActor>(new OneForOneStrategy(e => Directive.Stop));
            var rootGuardianActorRef = new RootGuardianActorRef((ActorSystemImpl)Sys, props, _dispatcher, Sys.Mailboxes.GetMailboxType(props, _dispatcher.Configurator.Config), ActorRefs.Nobody, _rootActorPath, _deadLetters, _emptyExtraNames);
            var actorRef = rootGuardianActorRef.GetSingleChild("deadLetters");
            Assert.Same(_deadLetters, actorRef);
        }


        [Fact]
        public void Getting_a_child_that_exists_in_extraNames_Should_return_the_child()
        {
            var extraNameChild = new DummyActorRef(_rootActorPath / "extra");
            var extraNames = new Dictionary<string, IInternalActorRef> { { "extra", extraNameChild } };
            var props = Props.Create<GuardianActor>(new OneForOneStrategy(e => Directive.Stop));
            var rootGuardianActorRef = new RootGuardianActorRef((ActorSystemImpl)Sys, props, _dispatcher, Sys.Mailboxes.GetMailboxType(props, _dispatcher.Configurator.Config), ActorRefs.Nobody, _rootActorPath, _deadLetters, extraNames);
            var actorRef = rootGuardianActorRef.GetSingleChild("extra");
            Assert.Same(extraNameChild, actorRef);
        }

        [Fact]
        public void Getting_an_unknown_child_that_exists_in_extraNames_Should_return_nobody()
        {
            var props = Props.Create<GuardianActor>(new OneForOneStrategy(e => Directive.Stop));
            var rootGuardianActorRef = new RootGuardianActorRef((ActorSystemImpl)Sys, props, _dispatcher, Sys.Mailboxes.GetMailboxType(props, _dispatcher.Configurator.Config), ActorRefs.Nobody, _rootActorPath, _deadLetters, _emptyExtraNames);
            var actorRef = rootGuardianActorRef.GetSingleChild("unknown-child");
            Assert.Same(ActorRefs.Nobody, actorRef);
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

            public override IActorRefProvider Provider
            {
                get { throw new System.NotImplementedException(); }
            }
        }
    }
}

