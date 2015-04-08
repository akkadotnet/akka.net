//-----------------------------------------------------------------------
// <copyright file="HoconLiteral.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace Akka.Configuration.Hocon
{
    public class HoconLiteral : IHoconElement
    {
        public string Value { get; set; }

        public bool IsString()
        {
            return true;
        }

        public string GetString()
        {
            return Value;
        }

        public bool IsArray()
        {
            return false;
        }

        public IList<HoconValue> GetArray()
        {
            throw new NotImplementedException();
        }

        public override string ToString()
        {
            return Value;
        }
    }
}

