//-----------------------------------------------------------------------
// <copyright file="HashReply.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

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
