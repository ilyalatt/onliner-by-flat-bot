namespace OnlinerByFlatBot.Extensions
{
    public static class StringExtensions
    {
        public static string TrimStart(this string s, string trim) =>
            s.StartsWith(trim) ? s.Substring(trim.Length) : s;
    }
}