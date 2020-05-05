//-----------------------------------------------------------------------
// <copyright file="Snapshot.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using System.Text;
using Akka.Actor;
using Akka.Serialization;
using Akka.Util;

namespace Akka.Persistence.Serialization
{
    /// <summary>
    /// Wrapper for snapshot data.
    /// </summary>
    public sealed class Snapshot
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="data">TBD</param>
        public Snapshot(object data)
        {
            Data = data;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public object Data { get; private set; }

        /// <inheritdoc/>
        private bool Equals(Snapshot other)
        {
            return Equals(Data, other.Data);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is Snapshot && Equals((Snapshot)obj);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return (Data != null ? Data.GetHashCode() : 0);
        }
    }
}
