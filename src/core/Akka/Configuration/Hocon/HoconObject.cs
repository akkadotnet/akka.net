using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace Akka.Configuration.Hocon
{
    public class HoconObject : IHoconElement
    {
        public HoconObject()
        {
            Items = new Dictionary<string, HoconValue>();
        }

        [JsonIgnore]
        public IDictionary<string, object> Unwrapped
        {
            get
            {
                return Items.ToDictionary(k => k.Key, v =>
                {
                    HoconObject obj = v.Value.GetObject();
                    if (obj != null)
                        return (object) obj.Unwrapped;
                    return v.Value;
                });
            }
        }

        public Dictionary<string, HoconValue> Items { get; private set; }

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

        public HoconValue GetKey(string key)
        {
            if (Items.ContainsKey(key))
            {
                return Items[key];
            }
            return null;
        }

        public HoconValue GetOrCreateKey(string key)
        {
            if (Items.ContainsKey(key))
            {
                return Items[key];
            }
            var child = new HoconValue();
            Items.Add(key, child);
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
            foreach (var kvp in Items)
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