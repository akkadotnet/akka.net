//-----------------------------------------------------------------------
// <copyright file="Cleanup.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.IO;
using Akka.Actor;

namespace Akka.Persistence.TestKit.Tests
{
    public static class Cleanup
    {
        public static void CreateStorageLocations(this ActorSystem system, string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }

        public static void DeleteStorageLocations(this ActorSystem system, string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
    }
}

