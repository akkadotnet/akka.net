using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using dm = Akka.DistributedData.Messages;

namespace Akka.DistributedData.Proto
{
    public class OtherMessageComparator : IComparer<dm.OtherMessage>
    {
        public int Compare(dm.OtherMessage x, dm.OtherMessage y)
        {
            var abytestring = x.EnclosedMessage;
            var bbytestring = y.EnclosedMessage;
            var asize = abytestring.Length;
            var bsize = bbytestring.Length;
            if(asize == bsize)
            {
                var aEnum = abytestring.GetEnumerator();
                var bEnum = bbytestring.GetEnumerator();
                while(true)
                {
                    if(aEnum.MoveNext() && bEnum.MoveNext())
                    {
                        if(aEnum.Current < bEnum.Current)
                        {
                            return -1;
                        }
                        else if(aEnum.Current > bEnum.Current)
                        {
                            return 1;
                        }
                        else
                        {
                            continue;
                        }
                    }
                    else
                    {
                        return 0;
                    }
                }
            }
            else if(asize < bsize)
            {
                return -1;
            }
            else
            {
                return 1;
            }
        }
    }
}
