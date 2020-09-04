//-----------------------------------------------------------------------
// <copyright file="DateTimeExtension.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

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
