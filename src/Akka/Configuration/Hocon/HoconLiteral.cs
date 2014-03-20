﻿using System;
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