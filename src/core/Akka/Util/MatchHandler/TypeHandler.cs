//-----------------------------------------------------------------------
// <copyright file="TypeHandler.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;

namespace Akka.Tools.MatchHandler
{
    /// <summary>
    /// TBD
    /// </summary>
    internal class TypeHandler
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TypeHandler"/> class.
        /// </summary>
        /// <param name="handlesType">TBD</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown if the given <paramref name="handlesType"/> is undefined.
        /// </exception>
        public TypeHandler(Type handlesType)
        {
            if(handlesType == null) throw new ArgumentNullException(nameof(handlesType), "Type cannot be null");
            HandlesType = handlesType;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public Type HandlesType { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public List<PredicateAndHandler> Handlers { get; } = new List<PredicateAndHandler>();

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public IEnumerable<Argument> GetArguments()
        {
            return Handlers.SelectMany(h => h.Arguments);
        } 
    }
}

