//-----------------------------------------------------------------------
// <copyright file="PersistenceTestKit.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Persistence.TestKit
{
    using Akka.TestKit.Xunit2;

    public abstract class PersistenceTestKit : PersistenceTestKitBase
    {
        protected PersistenceTestKit(string actorSystemName = null, string testActorName = null)
            : base(new XunitAssertions(), actorSystemName, testActorName)
        {
        }
    }
}