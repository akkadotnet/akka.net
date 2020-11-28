using System;
using System.Collections;
using System.Collections.Generic;

namespace Akka.Persistence.Sql.Linq2Db.Query
{
    /// <summary>
    /// A Class used to store ranges of numbers held
    /// more efficiently than full arrays.
    /// </summary>
    public class NumericRangeEntry: IEnumerable<long>
    {
        public NumericRangeEntry(long from, long until)
        {
            this.from = from;
            this.until = until;
        }
        public long from { get; }
        public long until { get;}

        public bool InRange(long number)
        {
            return  from <= number && number <= until;
        }

        public IEnumerable<long> ToEnumerable()
        {
            var itemCount = until - from;
            List<long> returnList;
            if (itemCount < Int32.MaxValue)
            {
                returnList = new List<long>();
            }
            else
            {
                returnList = new List<long>();
            }
            
            for (long i = from; i < until; i++)
            {
                returnList.Add(i);
            }

            return returnList;
        }

        public IEnumerator<long> GetEnumerator()
        {
            return ToEnumerable().GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}