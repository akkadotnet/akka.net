//-----------------------------------------------------------------------
// <copyright file="DictionaryExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;

namespace Akka.Util.Internal
{
    internal static class DictionaryExtensions
    {
        public static void Put<TKey, TVal>(this IDictionary<TKey, TVal> dict, TKey key, TVal value)
        {
            if (dict.ContainsKey(key))
                dict[key] = value;
            else
                dict.Add(key, value);
        }
    }
}
