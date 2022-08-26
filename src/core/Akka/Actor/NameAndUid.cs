﻿//-----------------------------------------------------------------------
// <copyright file="NameAndUid.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Actor
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    [Obsolete("Not used. Will be removed in Akka.NET v1.5.")]
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

