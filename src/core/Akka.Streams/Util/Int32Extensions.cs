//-----------------------------------------------------------------------
// <copyright file="Int32Extensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Streams.Util
{
    /// <summary>
    /// TBD
    /// </summary>
    public static class Int32Extensions
    {
        // see http://stackoverflow.com/questions/10439242/count-leading-zeroes-in-an-int32
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="x">TBD</param>
        /// <returns>TBD</returns>
        internal static int NumberOfLeadingZeros(this int x)
        {
            x |= (x >> 1);
            x |= (x >> 2);
            x |= (x >> 4);
            x |= (x >> 8);
            x |= (x >> 16);

            x -= (x >> 1) & 0x55555555;
            x = ((x >> 2) & 0x33333333) + (x & 0x33333333);
            x = ((x >> 4) + x) & 0x0f0f0f0f;
            x += x >> 8;
            x += x >> 16;
            x = x & 0x0000003f; // number of ones

            return sizeof (int)*8 - x;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="i">TBD</param>
        /// <returns>TBD</returns>
        internal static int NumberOfTrailingZeros(this int i)
        {
            if (i == 0)
                return 32;

            var x = (i & -i) - 1;
            x -= ((x >> 1) & 0x55555555);
            x = (((x >> 2) & 0x33333333) + (x & 0x33333333));
            x = (((x >> 4) + x) & 0x0f0f0f0f);
            x += (x >> 8);
            x += (x >> 16);
            return (x & 0x0000003f);
        }
    }
}
