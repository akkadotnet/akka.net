//-----------------------------------------------------------------------
// <copyright file="MemberAgeOrdering.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        private readonly bool _considerAppVersion;

        private MemberAgeOrdering(bool ascending, bool considerAppVersion)
        {
            _ascending = ascending;
            _considerAppVersion = considerAppVersion;
        }

        /// <inheritdoc/>
        public int Compare(Member x, Member y)
        {
            if (_considerAppVersion)
            {
                // prefer nodes with the highest app version, even if they're younger
                var appVersionDiff = x.AppVersion.CompareTo(y.AppVersion);
                if (appVersionDiff != 0)
                    return _ascending ? appVersionDiff : appVersionDiff * -1;
            }
            
            if (x.Equals(y)) return 0;
            return x.IsOlderThan(y)
                ? (_ascending ? 1 : -1)
                : (_ascending ? -1 : 1);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public static readonly MemberAgeOrdering Ascending = new MemberAgeOrdering(true, false);

        public static readonly MemberAgeOrdering AscendingWithAppVersion = new MemberAgeOrdering(true, true);

        /// <summary>
        /// TBD
        /// </summary>
        public static readonly MemberAgeOrdering Descending = new MemberAgeOrdering(false, false);
        
        public static readonly MemberAgeOrdering DescendingWithAppVersion = new MemberAgeOrdering(false, true);
    }
}
