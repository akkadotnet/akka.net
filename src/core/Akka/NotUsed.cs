//-----------------------------------------------------------------------
// <copyright file="NotUsed.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka
{
    /// <summary>
    /// This type is used in generic type signatures wherever the actual value is of no importance.
    /// It is a combination of F#’s 'unit' and C#’s 'void', which both have different issues when
    /// used from the other language. An example use-case is the materialized value of an Akka Stream for cases
    /// where no result shall be returned from materialization.
    /// </summary>
    [Serializable]
    public sealed class NotUsed : IEquatable<NotUsed>, IComparable<NotUsed>
    {
        /// <summary>
        /// The singleton instance of <see cref="NotUsed"/>.
        /// </summary>
        public static readonly NotUsed Instance = new NotUsed();

        private NotUsed()
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override int GetHashCode()
        {
            return 0;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="obj">TBD</param>
        /// <returns>TBD</returns>
        public override bool Equals(object obj)
        {
            return obj is NotUsed;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            return "()";
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public bool Equals(NotUsed other)
        {
            return true;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public int CompareTo(NotUsed other)
        {
            return 0;
        }
    }
}