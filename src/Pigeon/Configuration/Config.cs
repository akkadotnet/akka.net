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
        private HoconValue node;
        private Config fallback;

        public Config(HoconValue node)
        {
            this.node = node;
        }

        public Config(Config source,Config fallback)
        {
            this.node = source.node;
            this.fallback = fallback;
        }

        private HoconValue GetNode(string path)
        {
            var elements = path.Split('.');
            var node = this.node;
            foreach (var key in elements)
            {
                node = node.GetChildObject(key);
                if (node == null)
                {
                    if (fallback != null)
                        return fallback.GetNode(path);

                    return null;
                }
            }
            return node;
        }

        public bool GetBoolean(string path, bool @default = false)
        {
            var node = GetNode(path);
            if (node == null)
                return @default;

            return node.GetBoolean();
        }

        public int GetInt(string path,int @default=0)
        {
            var node = GetNode(path);
            if (node == null)
                return @default;

            return node.GetInt();
        }

        public long GetLong(string path, long @default = 0)
        {
            var node = GetNode(path);
            if (node == null)
                return @default;

            return node.GetLong();
        }

        public string GetString(string path,string @default=null)
        {
        
            var node = GetNode(path);
            if (node == null)
                return @default;

            return (string)node.GetString();
        }

        public float GetFloat(string path, float @default = 0)
        {
            var node = GetNode(path);
            if (node == null)
                return @default;

            return node.GetFloat();
        }

        public decimal GetDecimal(string path, decimal @default = 0)
        {
            var node = GetNode(path);
            if (node == null)
                return @default;

            return node.GetDecimal();
        }

        public double GetDouble(string path, double @default = 0)
        {
            var node = GetNode(path);
            if (node == null)
                return @default;

            return node.GetDouble();
        }

        public IList<Boolean> GetBooleanList(string path)
        {
            var node = GetNode(path);
            return node.GetBooleanList();
        }

        public IList<decimal> GetDecimalList(string path)
        {
            var node = GetNode(path);
            return node.GetDecimalList();
        }

        public IList<float> GetFloatList(string path)
        {
            var node = GetNode(path);
            return node.GetFloatList();
        }

        public IList<double> GetDoubleList(string path)
        {
            var node = GetNode(path);           
            return node.GetDoubleList();
        }

        public IList<int> GetIntList(string path)
        {
            var node = GetNode(path);
            return node.GetIntList();
        }

        public IList<long> GetLongList(string path)
        {
            var node = GetNode(path);
            return node.GetLongList();
        }

        public IList<byte> GetByteList(string path)
        {
            var node = GetNode(path);
            return node.GetByteList();
        }

        public IList<string> GetStringList(string path)
        {
            var node = GetNode(path);
            return node.GetStringList();
        }

        public Config GetConfig(string path)
        {
            var node = GetNode(path);
            return new Config(node);
        }

        public HoconValue GetValue(string path)
        {
            var node = GetNode(path);
            return node;
        }

        public TimeSpan GetMillisDuration(string path,TimeSpan? @default=null)
        {
            var node = GetNode(path);
            if (node == null)
                return @default.Value;

            return node.GetMillisDuration();
        }

        public override string ToString()
        {
            return this.node.ToString();
        }

        public Config WithFallback(Config fallback)
        {
            return new Config(this, fallback);
        }
    }
}
