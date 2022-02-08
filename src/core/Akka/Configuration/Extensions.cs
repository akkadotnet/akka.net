// //-----------------------------------------------------------------------
// // <copyright file="Extensions.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System.Linq;
using Hocon;
using Hocon.Abstraction;

namespace Akka.Configuration
{
    public static class Extensions
    {
        public static Config ToConfig(this IHoconValue value)
        {
            return new Config(new HoconRoot(value, Enumerable.Empty<HoconSubstitution>()));
        }

        /// <summary>
        /// Wraps this <see cref="HoconValue"/> into a new <see cref="Config"/> object at the specified key.
        /// </summary>
        /// <param name="key">The key designated to be the new root element.</param>
        /// <returns>A <see cref="Config"/> with the given key as the root element.</returns>
        public static Config AtKey(this IHoconValue value, string key)
        {
            var o = new HoconObject();
            o.GetOrCreateKey(key);
            o.Items[key] = value;
            var r = new HoconValue();
            r.Values.Add(o);
            return new Config(new HoconRoot(r));
        }

    }
}