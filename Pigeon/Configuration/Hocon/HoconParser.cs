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

        private HoconTokenizer reader;
        private HoconKeyValuePair ParseText(string text)
        {
            var root = new HoconKeyValuePair();
            reader = new HoconTokenizer(text);
            ParseObject( root.Content,true);
            return root;
        }

        private void ParseObject( HoconValue owner,bool root)
        {
            if (owner.IsObject())
            {
                //the value of this KVP is already an object
            }
            else
            {      
                //the value of this KVP is not an object, thus, we should add a new
                owner.NewValue(new HoconObject()); 
            }

            var currentObject = owner.GetObject();

            while (!reader.EoF)
            {
                Token t = reader.PullNext();
                switch (t.Type)
                {
                    case TokenType.EoF:
                        break;
                    case TokenType.Key:
                        var value = currentObject.GetOrCreateKey(t.Value.ToString());
                        ParseKeyContent( value);
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

        private void ParseKeyContent(HoconValue self)
        {
            while (!reader.EoF)
            {
                Token t = reader.PullNext();
                switch (t.Type)
                {
                    case TokenType.Dot:
                        ParseObject(self,false);
                        return; 
                    case TokenType.Assign:
                        ParseValue(reader, self);
                        return;
                    case TokenType.ObjectStart:
                        ParseObject( self,true);
                        return;
                }
            }            
        }

        public void ParseValue(HoconTokenizer reader, HoconValue owner)
        {
            while (!reader.EoF)
            {
                Token t = reader.PullNextValue();
                switch (t.Type)
                {
                    case TokenType.EoF:
                        break;
                    case TokenType.LiteralValue:
                        ParseSimpleValue( t.Value, owner);
                        return;
                    case TokenType.ObjectStart:
                        ParseObject( owner,true);
                        return;
                    case TokenType.ArrayStart:
                        var arr = ParseArray();
                        owner.NewValue(arr);
                        return;
                }
            }
        }

        public HoconArray ParseArray()
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
                        arr.Add(v);
                        ParseSimpleValue( t.Value, v);
                        break;
                    case TokenType.ObjectStart:
                        var c = new HoconValue();
                        ParseObject( c,true);
                        arr.Add(c);
                        break;
                    case TokenType.ArrayStart:
                        var childArr = ParseArray();
                        arr.Add(childArr);
                        break;
                    case TokenType.ArrayEnd:
                        reader.PullWhitespace();
                        IgnoreComma();

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

        private void ParseSimpleValue(object value, HoconValue v)
        {
            v.NewValue(value);
            while (reader.IsStartSimpleValue()) //fetch rest of values if string concat
            {
                var t = reader.PullSimpleValue();
                v.AppendValue(t.Value);
            }
            IgnoreComma();
        }

        private void IgnoreComma()
        {
            if (reader.IsComma()) //optional end of value
            {
                reader.PullComma();
            }
        }
    }
}