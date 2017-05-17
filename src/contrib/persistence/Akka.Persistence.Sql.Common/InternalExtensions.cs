//-----------------------------------------------------------------------
// <copyright file="InternalExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Reflection;

namespace Akka.Persistence.Sql.Common
{
    /// <summary>
    /// TBD
    /// </summary>
    internal static class InternalExtensions
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="type">TBD</param>
        /// <returns>TBD</returns>
        public static string QualifiedTypeName(this Type type)
        {
            return type.FullName + ", " + type.GetTypeInfo().Assembly.GetName().Name;
        }
    }
}