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
    internal static class InternalExtensions
    {
        public static string QualifiedTypeName(this Type type)
        {
            return type.FullName + ", " + type.GetTypeInfo().Assembly.GetName().Name;
        }
    }
}