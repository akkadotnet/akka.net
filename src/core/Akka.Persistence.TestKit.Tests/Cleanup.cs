using System.IO;
using Akka.Actor;

namespace Akka.Persistence.TestKit.Tests
{
    public static class Cleanup
    {
        public static void DeleteStorageLocations(this ActorSystem system, params string[] config)
        {
            foreach (var locationConfig in config)
            {
                var path = system.Settings.Config.GetString(locationConfig);
                var info = new DirectoryInfo(path);
                if (info.Exists)
                {
                    info.Delete(true);
                }
            }
        }
    }
}