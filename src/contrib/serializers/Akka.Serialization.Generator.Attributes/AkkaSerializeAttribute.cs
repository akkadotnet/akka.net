namespace Akka.Serialization;

[AttributeUsage(AttributeTargets.Class)]
public class AkkaSerializeAttribute : Attribute
{
    public AkkaSerializeAttribute(string manifest)
    {
        Manifest = manifest;
    }

    public string Manifest { get; }
}