//-----------------------------------------------------------------------
// <copyright file="CreateCustomSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Serialization;

namespace DocsExamples.Networking.Serialization
{
    #region CustomSerialization
    public class MySerializer : Serializer
    {
        public MySerializer(ExtendedActorSystem system) : base(system)
        {
        }

        /// <summary>
        /// This is whether <see cref="FromBinary"/> requires a <see cref="Type"/> or not
        /// </summary>
        public override bool IncludeManifest { get; } = false;

        /// <summary>
        /// Completely unique value to identify this implementation of the
        /// <see cref="Serializer"/> used to optimize network traffic
        /// 0 - 40 is reserved by Akka.NET itself
        /// </summary>
        public override int Identifier => 1234567;

        /// <summary>
        /// Serializes the given object to an Array of Bytes
        /// </summary>
        public override byte[] ToBinary(object obj)
        {
            // Put the real code that serializes the object here
            throw new NotImplementedException();
        }

        /// <summary>
        /// Deserializes the given array,  using the type hint (if any, see <see cref="IncludeManifest"/> above)
        /// </summary>
        public override object FromBinary(byte[] bytes, Type type)
        {
            // Put the real code that deserializes here
            throw new NotImplementedException();
        }
    }
    #endregion
}
