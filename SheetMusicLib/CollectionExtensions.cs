namespace SheetMusicLib
{
    /// <summary>
    /// Extension methods for collections (platform-independent)
    /// </summary>
    public static class CollectionExtensions
    {
        /// <summary>
        /// Binary search for first item >= key
        /// Returns -1 for empty list
        /// Returns list.Count if key > all items
        /// </summary>
        public static int FindIndexOfFirstGTorEQTo<T>(this IList<T> sortedList, T key) where T : IComparable<T>
        {
            int right;
            if (sortedList.Count == 0)
            {
                right = -1;
            }
            else
            {
                right = sortedList.Count - 1;
                int left = 0;
                while (right > left)
                {
                    var ndx = (left + right) / 2;
                    var elem = sortedList[ndx];
                    if (elem.CompareTo(key) >= 0)
                    {
                        right = ndx;
                    }
                    else
                    {
                        left = ndx + 1;
                    }
                }
            }
            if (right >= 0)
            {
                if (sortedList[right].CompareTo(key) < 0)
                {
                    right = sortedList.Count;
                }
            }
            return right;
        }
    }

    /// <summary>
    /// String utility methods
    /// </summary>
    public static class StringUtilities
    {
        /// <summary>
        /// Remove surrounding quotes from a string
        /// </summary>
        public static string RemoveQuotes(string str)
        {
            if (!string.IsNullOrEmpty(str))
            {
                if (str.StartsWith("\"") && str.EndsWith("\""))
                {
                    str = str.Replace("\"", string.Empty);
                }
            }
            return str;
        }
    }
}
