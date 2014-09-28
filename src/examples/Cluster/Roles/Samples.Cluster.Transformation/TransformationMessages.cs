namespace Samples.Cluster.Transformation
{
    public sealed class TransformationMessages
    {
        public class TransformationJob
        {
            public TransformationJob(string text)
            {
                Text = text;
            }

            public string Text { get; private set; }

            public override string ToString()
            {
                return Text;
            }
        }

        public class TransformationResult
        {
            public TransformationResult(string text)
            {
                Text = text;
            }

            public string Text { get; private set; }

            public override string ToString()
            {
                return string.Format("TransformationResult({0})", Text);
            }
        }

        public class JobFailed
        {
            public JobFailed(string reason, TransformationJob job)
            {
                Job = job;
                Reason = reason;
            }

            public string Reason { get; private set; }

            public TransformationJob Job { get; private set; }

            public override string ToString()
            {
                return string.Format("JobFailed({0})", Reason);
            }
        }

        public const string BACKEND_REGISTRATION = "BackendRegistration";
    }
}
