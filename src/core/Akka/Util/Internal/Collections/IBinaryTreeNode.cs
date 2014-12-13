namespace Akka.Util.Internal.Collections
{
	public interface IBinaryTreeNode<out TKey, out TValue>:IKeyValuePair<TKey,TValue>
	{
		IBinaryTreeNode<TKey, TValue> Left { get; }
		IBinaryTreeNode<TKey, TValue> Right { get; }
	}
}