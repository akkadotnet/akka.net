namespace Akka.Tools.MatchHandler
{
    /// <summary>
    /// An action that returns <c>true</c> if the <param name="item"/> was handled.
    /// </summary>
    /// <typeparam name="T">The type of the argument</typeparam>
    /// <param name="item">The argument.</param>
    /// <returns>Returns <c>true</c> if the <param name="item"/> was handled</returns>
    public delegate bool PartialAction<in T>(T item);
}