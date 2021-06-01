using System;
using System.Collections.Generic;
using System.Text;

namespace Akka.DistributedData.Serialization.Proto.Msg
{
    internal sealed partial class OtherMessage : IComparable<OtherMessage>
    {
        public int CompareTo(OtherMessage other)
        {
            if (ReferenceEquals(this, other)) return 0;
            if (ReferenceEquals(null, other)) return 1;

            var aByteString = EnclosedMessage.Span;
            var bByteString = other.EnclosedMessage.Span;
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
