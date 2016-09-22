//-----------------------------------------------------------------------
// <copyright file="TypeHandler.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;

namespace Akka.Tools.MatchHandler
{
    public class TypeHandler
    {
        private readonly Type _handlesType;
        private readonly List<PredicateAndHandler> _handlers = new List<PredicateAndHandler>();

        /// <summary>
        /// Initializes a new instance of the <see cref="TypeHandler"/> class.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown if the given <paramref name="handlesType"/> is undefined.
        /// </exception>
        public TypeHandler(Type handlesType)
        {
            if(handlesType == null) throw new ArgumentNullException(nameof(handlesType), "Type cannot be null");
            _handlesType = handlesType;
        }

        public Type HandlesType { get { return _handlesType; } }
        public List<PredicateAndHandler> Handlers { get { return _handlers; } }

        public IEnumerable<Argument> GetArguments()
        {
            return _handlers.SelectMany(h => h.Arguments);
        } 
    }
}

