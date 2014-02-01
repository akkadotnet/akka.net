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
            var node = this.node.Content.GetObject();
            foreach (var key in elements)
            {
                node = node.Content.GetChildObject(key);
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

            var v = node.Content.GetValue().ToString();
            switch(v)
            {
                case "on":
                    return true;
                case "off":
                    return false;
                case "true":
                    return true;
                case "false":
                    return false;
                default:
                    throw new NotSupportedException("Unknown boolean format: " + v);
            }
        }

        public int GetInt(string path,int @default=0)
        {
            var node = GetNode(path);
            if (node == null)
                return @default;

            return int.Parse(node.Content.GetValue().ToString(), NumberFormatInfo.InvariantInfo);
        }

        public long GetLong(string path, long @default = 0)
        {
            var node = GetNode(path);
            if (node == null)
                return @default;

            return long.Parse(node.Content.GetValue().ToString(), NumberFormatInfo.InvariantInfo);
        }

        public string GetString(string path,string @default=null)
        {
            var node = GetNode(path);
            if (node == null)
                return @default;

            return (string)node.Content.GetValue();
        }

        public double GetDouble(string path, double @default = 0)
        {
            var node = GetNode(path);
            if (node == null)
                return @default;

            return double.Parse(node.Content.GetValue().ToString(),NumberFormatInfo.InvariantInfo);
        }
    }
}
