//-----------------------------------------------------------------------
// <copyright file="TooManySubstreamsOpenException.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Streams
{
    /// <summary>
    /// This exception signals that the maximum number of substreams declared has been exceeded.
    /// A finite limit is imposed so that memory usage is controlled.
    /// </summary>
    public class TooManySubstreamsOpenException : InvalidOperationException
    {
        public TooManySubstreamsOpenException() :
            base("Cannot open a new substream as there are too many substreams open")
        { }
    }
}
