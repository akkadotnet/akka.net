//-----------------------------------------------------------------------
// <copyright file="ByteHelpers.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.IO;

namespace Akka.Util
{
    public static class ByteHelpers
    {
        public static byte[] PutInt(this byte[] target, int x, int offset = 0, ByteOrder order = ByteOrder.BigEndian)
        {
            if (order == ByteOrder.BigEndian)
            {
                target[offset + 0] = (byte)(x >> 24);
                target[offset + 1] = (byte)(x >> 16);
                target[offset + 2] = (byte)(x >> 8);
                target[offset + 3] = (byte)(x >> 0);
            }
            else
            {
                target[offset + 0] = (byte)(x >> 0);
                target[offset + 1] = (byte)(x >> 8);
                target[offset + 2] = (byte)(x >> 16);
                target[offset + 3] = (byte)(x >> 24);
            }

            return target;
        }
    }
}
