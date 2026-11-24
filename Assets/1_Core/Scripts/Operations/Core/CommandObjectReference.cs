using System;
using System.Text;

namespace Codetective.Operations
{
    /// <summary>The command spelling of a linked object. The UI displays the link's label instead.</summary>
    public static class CommandObjectReference
    {
        public static string Write(string id) => "@" + Convert.ToBase64String(Encoding.UTF8.GetBytes(id))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        public static bool TryRead(string token, out string id)
        {
            id = null;
            if (string.IsNullOrEmpty(token) || token[0] != '@' || token.Length < 2 || token.Length > 1369) return false;
            string encoded = token.Substring(1).Replace('-', '+').Replace('_', '/');
            try
            {
                id = Encoding.UTF8.GetString(Convert.FromBase64String(encoded.PadRight((encoded.Length + 3) / 4 * 4, '=')));
                return id.Length > 0;
            }
            catch (FormatException) { return false; }
        }

        public static bool IsCodeCharacter(char value) => value >= 'a' && value <= 'z' || value >= 'A' && value <= 'Z' ||
            value >= '0' && value <= '9' || value == '-' || value == '_';
    }
}
