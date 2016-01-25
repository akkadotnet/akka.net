//-----------------------------------------------------------------------
// <copyright file="ActorRefProviderSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
        public void CanResolveActorRef()
        {
            var path = TestActor.Path.ToString();
            var resolved = ((ActorSystemImpl)Sys).Provider.ResolveActorRef(path);
            Assert.Same(TestActor, resolved);
        }
    }
}

