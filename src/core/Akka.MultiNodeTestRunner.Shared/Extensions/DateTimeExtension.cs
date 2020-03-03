//-----------------------------------------------------------------------
// <copyright file="DateTimeExtension.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

#if CORECLR
namespace Akka.MultiNodeTestRunner.Shared.Extensions
{
    internal static class DateTimeExtension
    {
        public static string ToShortTimeString(this DateTime dateTime)
        {
            return dateTime.ToString("d");
        }
    }
}
#endif
