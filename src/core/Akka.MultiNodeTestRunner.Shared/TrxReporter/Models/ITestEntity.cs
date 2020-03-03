//-----------------------------------------------------------------------
// <copyright file="ITestEntity.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------


using System.Xml.Linq;

namespace Akka.MultiNodeTestRunner.TrxReporter.Models
{
    public interface ITestEntity
    {
        XElement Serialize();
    }
}
