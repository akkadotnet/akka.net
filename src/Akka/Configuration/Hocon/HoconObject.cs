using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Akka.Configuration.Hocon
{
    public class HoconObject : IHoconElement
    {
        private readonly Dictionary<string, HoconValue> _children = new Dictionary<string, HoconValue>();

        public IEnumerable<HoconValue> Children
        {
            get { return _children.Values; }
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

        public IDictionary<string, object> Unwrapped
        {
            get { return _children.ToDictionary(k => k.Key, v =>
            {
                var obj = v.Value.GetObject();
                if (obj != null)
                    return (object)obj.Unwrapped;
                return null;
            }); }
        }

        public IList<HoconValue> GetArray()
        {
            throw new NotImplementedException();
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

        public override string ToString()
        {
            return ToString(0);
        }

        public string ToString(int indent)
        {
            var i = new string(' ', indent*2);
            var sb = new StringBuilder();
            foreach (var kvp in _children)
            {
                string key = QuoteIfNeeded(kvp.Key);
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