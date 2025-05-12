// -----------------------------------------------------------------------
//  <copyright file="DownstreamCompletedWithNoCauseException.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;

namespace Akka.Streams.Implementation.Fusing;

public class DownstreamCompletedWithNoCauseException: Exception
{
    public DownstreamCompletedWithNoCauseException() : base("Downstream stage/flow completed with no cause")
    {
    }
}