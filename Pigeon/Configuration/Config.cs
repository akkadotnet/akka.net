using Pigeon.Configuration.Hocon;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Configuration
{
    public class Config
    {
        private HoconObject node;

        public Config(HoconObject node)
        {
            this.node = node;
        }

        private HoconObject GetNode(string path)
        {
            var elements = path.Split('.');
            var node = this.node;
            foreach (var part in elements)
            {
                node = node.Children.FirstOrDefault(n => n.Id == part);
                if (node == null)
                    return null;
            }
            return node;
        }

        public int GetInt(string path)
        {
            var node = GetNode(path);
            if (node == null)
                return 100;

            if (node.Value is long)
                return (int)(long)node.Value;

            return int.Parse(node.Value.ToString());
        }

        public string GetString(string path)
        {
            var node = GetNode(path);

            if (node.Value == null)
                return null;

            return node.Value.ToString();
        }
    }
}
