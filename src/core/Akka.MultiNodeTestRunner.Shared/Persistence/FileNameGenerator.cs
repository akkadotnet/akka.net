// -----------------------------------------------------------------------
//  <copyright file="FileNameGenerator.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;

namespace Akka.MultiNodeTestRunner.Shared.Persistence
{
    public class FileNameGenerator
    {
        public static string GenerateFileName(string assemblyName, string fileExtension)
        {
            return GenerateFileName(assemblyName, fileExtension, DateTime.UtcNow);
        }

        public static string GenerateFileName(string assemblyName, string fileExtension, DateTime utcNow)
        {
            return string.Format("{0}-{1}{2}", assemblyName.Replace(".dll", ""), utcNow.Ticks, fileExtension);
        }
    }
}