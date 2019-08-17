//-----------------------------------------------------------------------
// <copyright file="PersistenceTestKit.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Persistence.TestKit
{
    using Akka.TestKit.Xunit2;

    /// <summary>
    /// This class represents an Akka.NET Persistence TestKit that uses <a href="https://xunit.github.io/">xUnit</a>
    /// as its testing framework.
    /// </summary>
    public abstract class PersistenceTestKit : PersistenceTestKitBase
    {
        /// <summary>
        /// Create a new instance of the <see cref="PersistenceTestKit"/> class.
        /// A new system with the specified configuration will be created.
        /// </summary>
        /// <param name="actorSystemName">Optional: The name of the actor system</param>
        /// <param name="testActorName">Optional: The name of the TestActor.</param>
        protected PersistenceTestKit(string actorSystemName = null, string testActorName = null)
            : base(new XunitAssertions(), actorSystemName, testActorName)
        {
        }
    }
}