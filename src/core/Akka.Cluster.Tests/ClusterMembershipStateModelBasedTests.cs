using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;

namespace Akka.Cluster.Tests
{
    public class ClusterMembershipStateModelBasedTests
    {
    }

    public class ClusterMembershipModel
    {
        public ImmutableHashSet<Member> Members { get; }

        public ImmutableHashSet<Member> Unreachable { get; }

        public Member Leader
        {
            get
            {
                
            }
        }

        public ImmutableDictionary<Address, bool> HasConvergence { get; }
    }
}
