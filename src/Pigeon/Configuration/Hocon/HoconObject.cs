using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Configuration.Hocon
{
    public class HoconObject : IHoconElement
    {
        private readonly Dictionary<string, HoconValue> _children = new Dictionary<string, HoconValue>();
        public IEnumerable<HoconValue> Children
        {
            get { return _children.Values; }
        }

        public HoconValue GetKey(string key)
        {
            if (_children.ContainsKey(key))
            {
                return _children[key];
            }
            return null;
        }
        public HoconValue GetOrCreateKey(string key)
        {
            if (_children.ContainsKey(key))
            {
                return _children[key];
            }
            var child = new HoconValue();            
            _children.Add(key, child);
            return child;
        }

        public bool IsString()
        {
            return false;
        }

        public string GetString()
        {
            throw new NotImplementedException();
        }

        public bool IsArray()
        {
            return false;
        }

        public IList<HoconValue> GetArray()
        {
            throw new NotImplementedException();
        }

        public override string ToString()
        {
            return this.ToString(0);
        }

        public string ToString(int indent)
        {
            var i = new string(' ', indent * 2);
            StringBuilder sb = new StringBuilder();
            foreach(var kvp in _children)
            {
                var key = QuoteIfNeeded(kvp.Key);
                sb.AppendFormat("{0}{1} : {2}\r\n", i, key, kvp.Value.ToString(indent));
            }
            return sb.ToString();
        }

        private string QuoteIfNeeded(string text)
        {
            if (text.ToCharArray().Intersect(" \t".ToCharArray()).Any())
            {
                return "\"" + text + "\"";
            }
            return text;
        }
    }
}
