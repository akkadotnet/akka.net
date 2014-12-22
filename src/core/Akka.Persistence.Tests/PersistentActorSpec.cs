using System;
using System.Collections.Generic;
using Akka.TestKit;
using Xunit;

namespace Akka.Persistence.Tests
{
    public partial class PersistentActorSpec : PersistenceSpec
    {
        public PersistentActorSpec(string config) : base(config)
        {
        }
    }
}