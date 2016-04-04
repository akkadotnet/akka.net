//-----------------------------------------------------------------------
// <copyright file="StringBuilderExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;

namespace Akka.Util.Internal
{
    /// <summary>
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    public static class StringBuilderExtensions
    {
        public static StringBuilder AppendJoin<T>(this StringBuilder sb, string separator, IEnumerable<T> values)
        {
            return AppendJoin(sb, separator, values, null);
        }

        public static StringBuilder AppendJoin<T>(this StringBuilder sb, string separator, IEnumerable<T> values, Action<StringBuilder, T, int> valueAppender)
        {
            if (values == null) return sb;
            if (separator == null) separator = "";
            if (valueAppender == null) valueAppender = DefaultAppendValue;

            using (var enumerator = values.GetEnumerator())
            {
                var index = 0;
                if (!enumerator.MoveNext())
                    return sb;

                // ReSharper disable CompareNonConstrainedGenericWithNull
                var current = enumerator.Current;
                if (current != null)
                    // ReSharper restore CompareNonConstrainedGenericWithNull
                {
                    valueAppender(sb, current, index);
                }

                while (enumerator.MoveNext())
                {
                    index++;
                    sb.Append(separator);
                    // ReSharper disable CompareNonConstrainedGenericWithNull
                    if (enumerator.Current != null)
                        // ReSharper restore CompareNonConstrainedGenericWithNull
                    {
                        valueAppender(sb, current, index);
                    }
                }
            }
            return sb;
        }
        private static void DefaultAppendValue<T>(StringBuilder sb, T value, int index)
        {
            var s = value.ToString();
            if (s != null)
                sb.Append(value);
        }
    }
}

