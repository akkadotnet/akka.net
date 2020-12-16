// //-----------------------------------------------------------------------
// // <copyright file="SqlServerSpecsFixture.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using Akka.Persistence.Sql.Linq2Db.Tests.Docker;
using Xunit;

namespace Akka.Persistence.Linq2Db.CompatibilityTests
{
    [CollectionDefinition("SqlServerSpec")]
    public sealed class SqlServerSpecsFixture : ICollectionFixture<SqlServerFixture>
    {
    }
}