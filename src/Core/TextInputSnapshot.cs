using System;
using System.Text;

namespace ProbablyFonts.Core
{
    internal sealed class TextInputSnapshot
    {
        public bool HasValue { get; private set; }
        public string Text { get; private set; }
        public int Revision { get; private set; }

        public bool Capture(string source, int start, int length)
        {
            source = source ?? string.Empty;
            if (!IsValidRange(source.Length, start, length)) return false;
            Set(start == 0 && length == source.Length ? source : source.Substring(start, length));
            return true;
        }

        public bool Capture(char[] source, int start, int length)
        {
            int available = source == null ? 0 : source.Length;
            if (!IsValidRange(available, start, length)) return false;
            Set(length == 0 ? string.Empty : new string(source, start, length));
            return true;
        }

        public bool Capture(StringBuilder source, int start, int length)
        {
            int available = source == null ? 0 : source.Length;
            if (!IsValidRange(available, start, length)) return false;
            Set(length == 0 ? string.Empty : source.ToString(start, length));
            return true;
        }

        public void Set(string value)
        {
            value = value ?? string.Empty;
            if (HasValue && string.Equals(Text, value, StringComparison.Ordinal)) return;
            Text = value;
            HasValue = true;
            Revision = unchecked(Revision + 1);
        }

        private static bool IsValidRange(int available, int start, int length)
        {
            return start >= 0 && length >= 0 && start <= available && length <= available - start;
        }
    }
}
