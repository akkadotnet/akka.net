//-----------------------------------------------------------------------
// <copyright file="OtherMessageComparer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;
using Akka.DistributedData.Serialization.Proto.Msg;

namespace Akka.DistributedData.Serialization
{
    internal class OtherMessageAndVersionComparer : IComparer<
        ValueTuple<OtherMessage, Proto.Msg.VersionVector>>
    {
        public static OtherMessageAndVersionComparer Instance { get; } = new();
        public int Compare(ValueTuple<OtherMessage, Proto.Msg.VersionVector> x, ValueTuple<OtherMessage, Proto.Msg.VersionVector> y)
        {
            return OtherMessageComparer.Instance.Compare(x.Item1, y.Item1);
        }
    }
    internal class OtherMessageComparer : IComparer<OtherMessage>
    {
        public static OtherMessageComparer Instance { get; } = new();

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

            //int j = 0;
            return aByteString.SequenceCompareTo(bByteString); 
            //while (j + 4 < aSize)
            //{
            //    if (aByteString.Slice(j, 4)
            //            .SequenceEqual(bByteString.Slice(j, 4)) == false)
            //    {
            //        break;
            //    }
            //    else
            //    {
            //        j = j + 4;
            //    }
            //}
            //for (; j < aSize; j++)
            //{
            //    var aByte = aByteString[j];
            //    var bByte = bByteString[j];
            //    if (aByte < bByte) return -1;
            //    if (aByte > bByte) return 1;
            //}
            //for (var i = 0; i < aSize; i++)
            //{
            //    var aByte = aByteString[i];
            //    var bByte = bByteString[i];
            //    if (aByte < bByte) return -1;
            //    if (aByte > bByte) return 1;
            //}

            //return 0;
        }
    }
}
