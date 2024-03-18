namespace VMFInstanceInserter;
internal static class DictionaryExtensions
{
    public static T GetOrAdd<TKey, T>(this Dictionary<TKey, T> dict, TKey key, Func<TKey, T> generator)
    {
        if (dict.TryGetValue(key, out var value))
            return value;

        value = generator(key);
        dict.Add(key, value);
        return value;
    }
}
