//-----------------------------------------------------------------------
// <copyright file="HoconRoot.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;

namespace Akka.Configuration.Hocon
{
    public class HoconRoot
    {
        protected HoconRoot()
        {            
        }

        public HoconRoot(HoconValue value, IEnumerable<HoconSubstitution> substitutions)
        {
            Value = value;
            Substitutions = substitutions;
        }

        public HoconRoot(HoconValue value)
        {
            Value = value;
            Substitutions = Enumerable.Empty<HoconSubstitution>();
        }

        public HoconValue Value { get; private set; }
        public IEnumerable<HoconSubstitution> Substitutions { get; private set; }
    }
}

