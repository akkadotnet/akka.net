using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;

namespace Samples.Akka.AspNetCore.Messages
{
    /// <summary>
    /// Used to include both the hash and the actor who did the hashing, just for fun.
    /// </summary>
    public class HashReply
    {
        public HashReply(int hash, IActorRef hasher)
        {
            Hash = hash;
            Hasher = hasher;
        }

        public int Hash { get; }

        public IActorRef Hasher { get; }
    }
}
