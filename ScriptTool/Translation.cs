using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ScriptTool
{
    public class Translation
    {
        /// <summary>
        /// Loads translations from a file.
        /// Preserves all spaces, handles optional prefix, and converts literal "\n" to "￥".
        /// </summary>
        public static Dictionary<long, string> LoadTranslations(string filePath)
        {
            var translations = new Dictionary<long, string>();
            if (!File.Exists(filePath))
                return translations;

            int lineNumber = 0;
            foreach (var line in File.ReadLines(filePath, Encoding.UTF8))
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line) || !line.Contains("◆"))
                    continue;

                var parts = line.Split('◆');
                if (parts.Length < 3)
                    continue;

                if (!long.TryParse(parts[1], System.Globalization.NumberStyles.HexNumber, null, out long address))
                {
                    Console.WriteLine($"WARNING: Invalid address format at line {lineNumber}");
                    continue;
                }

                string text = parts[2];

                // If line contains prefix format like |prefix|text
                if (text.StartsWith("|"))
                {
                    int secondBar = text.IndexOf('|', 1);
                    if (secondBar > 1 && secondBar < text.Length - 1)
                    {
                        text = text.Substring(secondBar + 1); // remove prefix part
                    }
                }

                // Preserve all spaces, and convert literal "\n" to "￥"
                text = text.UnescapeText();
                translations[address] = text;
            }

            return translations;
        }

        /// <summary>
        /// Automatically inserts line breaks before maxLength, preserving spaces and "￥".
        /// </summary>
        public static string AutoLineBreak(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            var lines = text.Split("\\n");
            var wrapped = new List<string>();

            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line))
                {
                    wrapped.Add("");
                    continue;
                }

                string remaining = line;
                while (remaining.Length > maxLength)
                {
                    int breakPos = remaining.LastIndexOf(' ', Math.Min(maxLength, remaining.Length - 1));
                    
                    if (breakPos <= 0)
                        breakPos = Math.Min(maxLength, remaining.Length);

                    wrapped.Add(remaining.Substring(0, breakPos));
                    remaining = remaining.Substring(breakPos).TrimStart(); // Trim leading space
                }

                if (!string.IsNullOrEmpty(remaining))
                    wrapped.Add(remaining);
            }

            return string.Join("￥", wrapped);
        }
    }

}
