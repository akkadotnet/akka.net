using System.Collections.Generic;

namespace Akka.Cluster.Tools.Singleton
{
    internal sealed class MemberAgeOrdering : IComparer<Member>
    {
        private readonly bool _ascending;
        private MemberAgeOrdering(bool ascending)
        {
            _ascending = ascending;
        }

        public int Compare(Member x, Member y)
        {
            if (x.Equals(y)) return 0;
            return x.IsOlderThan(y)
                ? (_ascending ? 1 : -1)
                : (_ascending ? -1 : 1);
        }
        public static readonly MemberAgeOrdering Ascending = new MemberAgeOrdering(true);
        public static readonly MemberAgeOrdering Descending = new MemberAgeOrdering(false);
    }
}
