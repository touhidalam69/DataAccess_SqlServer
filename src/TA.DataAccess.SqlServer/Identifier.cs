using System.Text.RegularExpressions;

namespace TA.DataAccess.SqlServer
{
    internal static class Identifier
    {
        private static readonly Regex IdentifierRegex = new(
            @"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)?$",
            RegexOptions.Compiled);

        public static string Quote(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
                throw new ArgumentException("Identifier must not be null or empty.", nameof(identifier));
            if (!IdentifierRegex.IsMatch(identifier))
                throw new ArgumentException($"Invalid SQL identifier: '{identifier}'.", nameof(identifier));

            return string.Join(".", identifier.Split('.').Select(part => "[" + part.Replace("]", "]]") + "]"));
        }
    }
}
