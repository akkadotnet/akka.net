//-----------------------------------------------------------------------
// <copyright file="OtherMessageComparer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;
using Akka.DistributedData.Serialization.Proto.Msg;

namespace Akka.DistributedData.Serialization
{
    internal class OtherMessageComparer : IComparer<OtherMessage>
    {
        public static OtherMessageComparer Instance { get; } = new OtherMessageComparer();

        private OtherMessageComparer()
        {}

        public int Compare(OtherMessage a, OtherMessage b)
        {
            if (a == null || b == null)
                throw new Exception("Both messages must not be null");
            if (ReferenceEquals(a, b)) return 0;

            var aByteString = a.EnclosedMessage.Span;
            var bByteString = b.EnclosedMessage.Span;
            var aSize = aByteString.Length;
            var bSize = bByteString.Length;
            if (aSize < bSize) return -1;
            if (aSize > bSize) return 1;

            for (var i = 0; i < aSize; i++)
            {
                var aByte = aByteString[i];
                var bByte = bByteString[i];
                if (aByte < bByte) return -1;
                if (aByte > bByte) return 1;
            }

            return 0;
        }
    }
}
