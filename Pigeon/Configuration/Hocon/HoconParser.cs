using System.Collections.Generic;
using System.Linq;

namespace Pigeon.Configuration.Hocon
{
    public class Parser
    {
        public static HoconKeyValuePair Parse(string text)
        {
            return new Parser().ParseText(text);
        }

        private HoconKeyValuePair ParseText(string text)
        {
            var root = new HoconKeyValuePair();
            var reader = new HoconTokenizer(text);
            ParseObject(reader, root,true);
            return root;
        }

        private void ParseObject(HoconTokenizer reader, HoconKeyValuePair owner,bool root)
        {
            if (owner.Content.IsObject())
            {
                //the value of this KVP is already an object
            }
            else
            {      
                //the value of this KVP is not an object, thus, we should add a new
                owner.Content.NewValue(owner); //set self as content
                owner.Clear();
            }

            var currentObject = owner.Content.GetObject();

            while (!reader.EoF)
            {
                Token t = reader.PullNext();
                switch (t.Type)
                {
                    case TokenType.EoF:
                        break;
                    case TokenType.Key:
                        var childKVP = currentObject.GetOrCreateKey(t.Value.ToString());
                        ParseKeyContent(reader, childKVP);
                        if (!root)
                            return;
                        break;
                    
                    case TokenType.ObjectEnd:
                        reader.PullWhitespace();
                        if (reader.IsComma())
                        {
                            reader.PullComma();
                        }
                        return;
                }
            }
        }

        private void ParseKeyContent(HoconTokenizer reader, HoconKeyValuePair self)
        {
            while (!reader.EoF)
            {
                Token t = reader.PullNext();
                switch (t.Type)
                {
                    case TokenType.Dot:
                        ParseObject(reader, self,false);
                        return; 
                    case TokenType.Assign:
                        ParseValue(reader, self);
                        return;
                    case TokenType.ObjectStart:
                        ParseObject(reader, self,true);
                        return;
                }
            }
            
        }

        public void ParseValue(HoconTokenizer reader, HoconKeyValuePair context)
        {
            while (!reader.EoF)
            {
                Token t = reader.PullNextValue();
                switch (t.Type)
                {
                    case TokenType.EoF:
                        break;
                    case TokenType.LiteralValue:
                        context.Content.NewValue(t.Value);
                        while (reader.IsStartSimpleValue()) //fetch rest of values if string concat
                        {
                            t = reader.PullSimpleValue();
                            context.Content.AppendValue(t.Value);
                        }
                        if (reader.IsComma()) //optional end of value
                        {
                            reader.PullComma();
                        }

                        return;
                    case TokenType.ObjectStart:
                        ParseObject(reader, context,true);
                        return;
                    case TokenType.ArrayStart:
                        var arr = ParseArray(reader, context);
                        context.Content.NewValue(arr);
                        return;
                }
            }
        }

        public HoconArray ParseArray(HoconTokenizer reader, HoconKeyValuePair context)
        {
            var arr = new HoconArray();
            while (!reader.EoF)
            {
                Token t = reader.PullNextValue();
                switch (t.Type)
                {
                    case TokenType.EoF:
                        break;
                    case TokenType.LiteralValue:
                        HoconValue v = new HoconValue();
                        v.NewValue(t.Value);
                        arr.Add(v);
                        while (reader.IsStartSimpleValue()) //fetch rest of values if string concat
                        {
                            t = reader.PullSimpleValue();
                            v.AppendValue(t.Value);
                        }
                        if (reader.IsComma()) //optional end of value
                        {
                            reader.PullComma();
                        }
                        break;
                    case TokenType.ObjectStart:
                        var c = new HoconKeyValuePair();
                        ParseObject(reader, c,true);
                        arr.Add(c);
                        break;
                    case TokenType.ArrayStart:
                        var c2 = new HoconKeyValuePair();
                        var childArr = ParseArray(reader, c2);
                        arr.Add(childArr);
                        break;
                    case TokenType.ArrayEnd:
                        reader.PullWhitespace();
                        if (reader.IsComma())
                        {
                            reader.PullComma();
                        }

                        if (!arr.Any() || !arr.All(e => e is HoconArray)) 
                            return arr;

                        //concat arrays in arrays
                        var x = (from a in arr.OfType<HoconArray>()
                            from e in a 
                            select e).ToArray();
                        arr.Clear();
                        arr.AddRange(x);
                        return arr;
                }
            }
            return arr;
        }
    }
}