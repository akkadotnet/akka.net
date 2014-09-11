using System;

namespace Akka.Util
{
    public abstract class Either<TA,TB>
    {
        public abstract bool IsLeft { get; }
        public abstract bool IsRight { get; }
    }

    public class Right<TA, TB> : Either<TA, TB>
    {
        public Right(TB b)
        {
            throw new NotImplementedException();
        }

        public override bool IsLeft
        {
            get { return false; }
        }

        public override bool IsRight
        {
            get { return true; }
        }
    }

    public class Left<TA, TB> : Either<TA, TB>
    {
        public Left(TA a)
        {
            throw new NotImplementedException();
        }

        public override bool IsLeft
        {
            get { return true; }
        }

        public override bool IsRight
        {
            get { return false; }
        }
    }
}
