//-----------------------------------------------------------------------
// <copyright file="Int32Extensions.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Streams.Util
{
    public static class Int32Extensions
    {
        // see http://stackoverflow.com/questions/10439242/count-leading-zeroes-in-an-int32
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