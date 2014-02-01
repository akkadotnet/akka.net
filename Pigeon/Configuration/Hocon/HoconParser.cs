using System.Collections.Generic;
using System.Linq;

namespace Pigeon.Configuration.Hocon
{
    public class Parser
    {
        public static HoconValue Parse(string text)
        {
            return new Parser().ParseText(text);
        }

        private HoconTokenizer reader;
        private HoconValue ParseText(string text)
        {
            var root = new HoconValue();
            reader = new HoconTokenizer(text);
            reader.PullWhitespaceAndComments();
            ParseObject( root,true);
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

        private void ParseKeyContent(HoconValue value)
        {
            while (!reader.EoF)
            {
                Token t = reader.PullNext();
                switch (t.Type)
                {
                    case TokenType.Dot:
                        ParseObject(value,false);
                        return; 
                    case TokenType.Assign:
                        ParseValue(reader, value);
                        return;
                    case TokenType.ObjectStart:
                        ParseObject( value,true);
                        return;
                }
            }            
        }

        public void ParseValue(HoconTokenizer reader, HoconValue value)
        {
            while (!reader.EoF)
            {
                Token t = reader.PullNextValue();
                switch (t.Type)
                {
                    case TokenType.EoF:
                        break;
                    case TokenType.LiteralValue:
                        ParseSimpleValue( t.Value, value);
                        return;
                    case TokenType.ObjectStart:
                        ParseObject( value,true);
                        return;
                    case TokenType.ArrayStart:
                        var arr = ParseArray();
                        value.NewValue(arr);
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
                        {
                            var value = new HoconValue();
                            arr.Add(value);
                            ParseSimpleValue(t.Value, value);
                            break;
                        }
                    case TokenType.ObjectStart:
                        {
                            var value = new HoconValue();
                            arr.Add(value);
                            ParseObject(value, true);                            
                            break;
                        }
                    case TokenType.ArrayStart:
                        {
                            var childArr = ParseArray();
                            var value = new HoconValue();
                            value.NewValue(childArr);
                            arr.Add(value);
                            break;
                        }
                    case TokenType.ArrayEnd:
                        {
                            reader.PullWhitespace();
                            IgnoreComma();

                            if (!arr.Any() || !arr.All(e => e.IsArray()))
                                return arr;

                            //concat arrays in arrays
                            var x = (from a in arr
                                     from e in a.GetArray()
                                     select e).ToArray();
                            arr.Clear();
                            arr.AddRange(x);
                            return arr;
                        }
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