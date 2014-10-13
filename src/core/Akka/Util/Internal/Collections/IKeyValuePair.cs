namespace Akka.Util.Internal.Collections
{
	public interface IKeyValuePair<out TKey, out TValue>
	{
		TKey Key { get; }
		TValue Value { get; }
	}
}