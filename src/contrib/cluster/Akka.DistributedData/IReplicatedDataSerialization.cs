//-----------------------------------------------------------------------
// <copyright file="IReplicatedDataSerialization.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.DistributedData.Serialization;

namespace Akka.DistributedData
{
    /// <summary>
    /// Marker interface used to indicate that this message will be serialized by <see cref="ReplicatedDataSerializer"/>
    /// </summary>
    public interface IReplicatedDataSerialization
    {
        
    }
}
