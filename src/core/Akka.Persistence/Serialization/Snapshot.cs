//-----------------------------------------------------------------------
// <copyright file="Snapshot.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        /// Initializes a snapshot data wrapper.
        /// </summary>
        /// <param name="data">Snapshot data to wrap.</param>
        public Snapshot(object data)
        {
            Data = data;
        }

        /// <summary>
        /// Snapshot data wrapped by this object.
        /// </summary>
        public object Data { get; private set; }

       
        private bool Equals(Snapshot other)
        {
            return Equals(Data, other.Data);
        }

       
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is Snapshot snapshot && Equals(snapshot);
        }

       
        public override int GetHashCode()
        {
            return (Data != null ? Data.GetHashCode() : 0);
        }
    }
}
