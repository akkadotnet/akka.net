//-----------------------------------------------------------------------
// <copyright file="Comparable.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Newtonsoft.Json;

namespace Akka.Tests.TestUtils
{
    public class Comparable
    {
        public override bool Equals(object obj)
        {
            if (obj == null)
                return false;

            return this.ToString() == obj.ToString();
        }
        public override int GetHashCode()
        {
            return this.ToString().GetHashCode();
        }

        public override string ToString()
        {
            var res = JsonConvert.SerializeObject(this);
            return res;
        }
    }
}

