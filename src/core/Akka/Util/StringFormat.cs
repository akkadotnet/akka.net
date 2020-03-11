//-----------------------------------------------------------------------
// <copyright file="StringFormat.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;

namespace Akka.Util
{
    /// <summary>
    /// TBD
    /// </summary>
    public static class StringFormat
    {
        /// <summary>
        /// Concatenates the values, using the specified separator between the elements.
        /// This method is similar to <see cref="string.Join(string,object[])"/> except it
        /// formats null values as &lt;null&gt;
        /// </summary>
        /// <param name="separator">The separator.</param>
        /// <param name="args">The arguments.</param>
        /// <returns>TBD</returns>
        public static string SafeJoin(string separator, params object[] args)
        {
            return string.Join(separator, ConvertValues(args));
        }

        private static object[] ConvertValues(IList<object> args)
        {
            var length=args.Count;
            var values = new object[length];
            for(var i = 0; i < length; i++)
            {
                var arg = args[i];
                values[i] = arg ?? "<null>";
            }
            return values;
        }
    }
}

