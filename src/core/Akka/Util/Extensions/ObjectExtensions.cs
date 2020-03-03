//-----------------------------------------------------------------------
// <copyright file="ObjectExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Util.Extensions
{
    public static class ObjectExtensions
    {
        /// <summary>
        /// Wraps object to the <see cref="Option{T}"/> monade
        /// </summary>
        public static Option<T> AsOption<T>(this T obj) => new Option<T>(obj);
    }
}
