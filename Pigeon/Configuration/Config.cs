using Pigeon.Configuration.Hocon;
using System;
using System.Collections.Generic;
using System.Globalization;
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

            return bool.Parse(node.Value.GetValue().ToString());
        }

        public int GetInt(string path,int @default=0)
        {
            var node = GetNode(path);
            if (node == null)
                return @default;

            return int.Parse(node.Value.GetValue().ToString(), NumberFormatInfo.InvariantInfo);
        }

        public long GetLong(string path, long @default = 0)
        {
            var node = GetNode(path);
            if (node == null)
                return @default;

            return long.Parse(node.Value.GetValue().ToString(), NumberFormatInfo.InvariantInfo);
        }

        public string GetString(string path,string @default=null)
        {
            var node = GetNode(path);
            if (node == null)
                return @default;

            return node.Value.GetValue().ToString();
        }

        public double GetDouble(string path, double @default = 0)
        {
            var node = GetNode(path);
            if (node == null)
                return @default;

            return double.Parse(node.Value.GetValue().ToString(),NumberFormatInfo.InvariantInfo);
        }
    }
}
