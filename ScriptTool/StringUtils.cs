using System;
using System.Text;

namespace ScriptTool
{
    public static class StringUtils
    {
        /// <summary>
        /// Escapes text for export (game → text file).
        /// Converts internal newline markers (￥) to literal "\n".
        /// </summary>
        public static string EscapeText(this string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            return input
                .Replace("\\", "\\\\")   // escape literal backslashes
                .Replace("￥", "\\n");    // internal linebreak → visible "\n"
        }

        /// <summary>
        /// Unescapes text for import (text file → game).
        /// Converts literal "\n" back into internal yen markers (￥).
        /// </summary>
        public static string UnescapeText(this string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            return input
                .Replace("\\n", "￥")     // visible "\n" → internal linebreak
                .Replace("\\\\", "\\");   // restore escaped backslashes
        }
    }
}
