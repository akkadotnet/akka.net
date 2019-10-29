// -----------------------------------------------------------------------
//  <copyright file="ITestEntity.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------
using System.Xml.Linq;

namespace Akka.MultiNodeTestRunner.TrxReporter.Models
{
    public interface ITestEntity
    {
        XElement Serialize();
    }
}