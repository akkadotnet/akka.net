using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Actor
{
    public class ActorPath
    {
        private List<string> parts = new List<string>();
        public ActorPath(string path)
        {
            parts = path.Split('/').ToList();
        }

        public ActorPath(string parentPath,string name)
        {
            parts = parentPath.Split('/').ToList();
            parts.Add(name);
        }

        public ActorPath(ActorPath parentPath, string name)
        {
            parts.AddRange(parentPath.parts);
            parts.Add(name);
        }
        public override string ToString()
        {
            return string.Join("/", parts);
        }
    }
}
