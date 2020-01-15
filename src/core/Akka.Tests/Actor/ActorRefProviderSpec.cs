//-----------------------------------------------------------------------
// <copyright file="ActorRefProviderSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor.Internal;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Actor
{
    
    public class ActorRefProviderSpec : AkkaSpec
    {
        [Fact]
        public void Can_resolve_ActorRef()
        {
            var path = TestActor.Path.ToString();
            var resolved = ((ActorSystemImpl)Sys).Provider.ResolveActorRef(path);
            Assert.Same(TestActor, resolved);
        }
    }
}

