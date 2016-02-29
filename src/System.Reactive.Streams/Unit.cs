namespace System.Reactive.Streams
{
    [Serializable]
    public sealed class Unit : IComparable
    {
        public static readonly Unit Instance = new Unit();

        private Unit()
        {
        }

        public override int GetHashCode()
        {
            return 0;
        }

        public int CompareTo(object obj)
        {
            return 0;
        }

        public override bool Equals(object obj)
        {
            if (obj is Unit || obj == null) return true;
            return false;
        }

        public override string ToString()
        {
            return "()";
        }
    }
}