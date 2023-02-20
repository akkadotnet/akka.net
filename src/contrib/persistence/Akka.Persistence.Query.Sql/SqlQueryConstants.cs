// //-----------------------------------------------------------------------
// // <copyright file="SqlQueryConstants.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;

namespace Akka.Persistence.Query.Sql
{
    public static class SqlQueryConstants
    {
        public static readonly TimeSpan DefaultQueryTimeout = TimeSpan.FromSeconds(10);
    }
}