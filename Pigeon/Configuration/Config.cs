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

        public bool GetBoolean(string path, bool @default = false)
        {
            var node = GetNode(path);
            if (node == null)
                return @default;

            if (node.Value is bool)
                return (bool)node.Value;

            return bool.Parse(node.Value.ToString());
        }

        public int GetInt(string path,int @default=0)
        {
            var node = GetNode(path);
            if (node == null)
                return @default;

            if (node.Value is long)
                return (int)(long)node.Value;

            return int.Parse(node.Value.ToString());
        }

        public long GetLong(string path, long @default = 0)
        {
            var node = GetNode(path);
            if (node == null)
                return @default;

            if (node.Value is long)
                return (long)node.Value;

            return long.Parse(node.Value.ToString());
        }

        public string GetString(string path,string @default=null)
        {
            var node = GetNode(path);
            if (node == null)
                return @default;

            if (node.Value == null)
                return null;

            return node.Value.ToString();
        }
    }
}
