//-----------------------------------------------------------------------
// <copyright file="TagWriteMode.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
namespace Akka.Persistence.Embedded
{
    /// <summary>
    /// Where the journal stores event tags. Same three modes as Akka.Persistence.Sql.
    /// </summary>
    public enum TagWriteMode
    {
        /// <summary>Tags go into a delimited <c>tags</c> column of the journal table.</summary>
        Csv,

        /// <summary>Tags go into the separate <c>tags</c> table, one row per tag. This is the default.</summary>
        TagTable,

        /// <summary>Tags go into both places.</summary>
        Both
    }
}
