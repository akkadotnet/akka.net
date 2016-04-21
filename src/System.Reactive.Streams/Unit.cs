//-----------------------------------------------------------------------
// <copyright file="Unit.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

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