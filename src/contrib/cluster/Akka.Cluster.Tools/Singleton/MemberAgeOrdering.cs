//-----------------------------------------------------------------------
// <copyright file="MemberAgeOrdering.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;

namespace Akka.Cluster.Tools.Singleton
{
    /// <summary>
    /// TBD
    /// </summary>
    internal sealed class MemberAgeOrdering : IComparer<Member>
    {
        private readonly bool _ascending;

        private MemberAgeOrdering(bool ascending)
        {
            _ascending = ascending;
        }

        /// <inheritdoc/>
        public int Compare(Member x, Member y)
        {
            if (x.Equals(y)) return 0;
            return x.IsOlderThan(y)
                ? (_ascending ? 1 : -1)
                : (_ascending ? -1 : 1);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public static readonly MemberAgeOrdering Ascending = new MemberAgeOrdering(true);

        /// <summary>
        /// TBD
        /// </summary>
        public static readonly MemberAgeOrdering Descending = new MemberAgeOrdering(false);
    }
}
