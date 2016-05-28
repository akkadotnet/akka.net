//-----------------------------------------------------------------------
// <copyright file="StageException.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Streams
{
    public class NoSuchElementException : Exception
    {
        public NoSuchElementException(string message) : base(message)
        {

        }
    }
}
