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