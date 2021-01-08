//-----------------------------------------------------------------------
// <copyright file="NameAndUid.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Actor
{
    /// <summary>
    /// TBD
    /// </summary>
    public class NameAndUid
    {
        private readonly string _name;
        private readonly int _uid;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        /// <param name="uid">TBD</param>
        public NameAndUid(string name, int uid)
        {
            _name = name;
            _uid = uid;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public string Name { get { return _name; } }

        /// <summary>
        /// TBD
        /// </summary>
        public int Uid { get { return _uid; } }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            return _name + "#" + _uid;
        }
    }
}

