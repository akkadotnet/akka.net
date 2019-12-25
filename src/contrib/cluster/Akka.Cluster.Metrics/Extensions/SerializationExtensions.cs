// //-----------------------------------------------------------------------
// // <copyright file="SerializationExtensions.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.Cluster.Metrics.Extensions
{
    public static class SerializationExtensions
    {
        /// <summary>
        /// Converts Akka.NET type into Protobuf serializable message
        /// </summary>
        public static Serialization.Address ToProto(this Address address)
        {
            return new Serialization.Address()
            {
                Hostname = address.Host,
                Protocol = address.Protocol,
                Port = (uint)address.Port,
                System = address.System
            };
        }
        
        /// <summary>
        /// ConvertsProtobuf serializable message to Akka.NET type
        /// </summary>
        /// <param name="address"></param>
        /// <returns></returns>
        public static Akka.Actor.Address FromProto(this Serialization.Address address)
        {
            return new Address(address.Protocol, address.System, address.Hostname, (int)address.Port);
        }
    }
}