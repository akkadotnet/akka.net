//-----------------------------------------------------------------------
// <copyright file="Done.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading.Tasks;

namespace Akka
{
    /// <summary>
    /// Used with <see cref="Task"/> instances to signal completion,
    /// but there is no actual value completed. 
    /// </summary>
    public sealed class Done
    {
        /// <summary>
        /// The singleton instance of <see cref="Done"/>
        /// </summary>
        public static readonly Done Instance = new Done();

        private Done() { }
    }
}
