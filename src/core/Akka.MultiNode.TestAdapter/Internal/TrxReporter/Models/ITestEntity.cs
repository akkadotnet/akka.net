// -----------------------------------------------------------------------
//  <copyright file="ITestEntity.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System.Xml.Linq;

namespace Akka.MultiNode.TestAdapter.Internal.TrxReporter.Models
{
    internal interface ITestEntity
    {
        XElement Serialize();
    }
}