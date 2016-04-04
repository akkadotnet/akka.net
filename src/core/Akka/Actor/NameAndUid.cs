//-----------------------------------------------------------------------
// <copyright file="NameAndUid.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Actor
{
    public class NameAndUid
    {
        private readonly string _name;
        private readonly int _uid;

        public NameAndUid(string name, int uid)
        {
            _name = name;
            _uid = uid;
        }

        public string Name { get { return _name; } }

        public int Uid { get { return _uid; } }

        public override string ToString()
        {
            return _name + "#" + _uid;
        }
    }
}

