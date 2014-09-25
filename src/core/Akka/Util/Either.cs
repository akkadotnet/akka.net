namespace Akka.Util
{
    public abstract class Either<TA,TB>
    {
        protected Either(TA left, TB right)
        {
            Left = left;
            Right = right;
        }

        public abstract bool IsLeft { get; }
        public abstract bool IsRight { get; }

        protected TB Right { get; private set; }

        protected TA Left { get; private set; }

        public object Value
        {
            get
            {
                if (IsLeft) return Left;
                return Right;
            }
        }
        public Right<TA, TB> ToRight()
        {
            return new Right<TA, TB>(Right);
        }

        public Left<TA, TB> ToLeft()
        {
            return new Left<TA, TB>(Left);
        }
    }

    public class Right<TA, TB> : Either<TA, TB>
    {
        public Right(TB b) : base(default(TA), b)
        {
        }

        public override bool IsLeft
        {
            get { return false; }
        }

        public override bool IsRight
        {
            get { return true; }
        }

        public new TB Value
        {
            get { return Right; }
        }
    }

    public class Left<TA, TB> : Either<TA, TB>
    {
        public Left(TA a) : base(a, default(TB))
        {
        }

        public override bool IsLeft
        {
            get { return true; }
        }

        public override bool IsRight
        {
            get { return false; }
        }

        public new TA Value
        {
            get { return Left; }
        }
    }
}
