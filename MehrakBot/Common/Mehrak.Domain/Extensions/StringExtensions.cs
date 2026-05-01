namespace Mehrak.Domain.Extensions;

public static class StringExtensions
{
    public static string ToTitleCase(this string str)
    {
        if (string.IsNullOrEmpty(str))
            return str;

        var source = str.AsSpan();

        // First pass: count words and determine exact output length
        var wordCount = 0;
        var outputLength = 0;
        var inWord = false;

        foreach (char c in source)
        {
            var isSeparator = c is ' ' or '_';

            if (isSeparator)
            {
                inWord = false;
            }
            else
            {
                if (!inWord)
                {
                    wordCount++;
                    inWord = true;
                }
                outputLength++;
            }
        }

        if (wordCount == 0)
            return string.Empty;

        outputLength += wordCount - 1; // spaces between words

        return string.Create(outputLength, str, (span, state) =>
        {
            var src = state.AsSpan();
            var newWord = true;
            var pos = 0;

            foreach (char c in src)
            {
                if (c is ' ' or '_')
                {
                    newWord = true;
                }
                else
                {
                    if (newWord)
                    {
                        if (pos > 0)
                            span[pos++] = ' ';

                        span[pos++] = char.ToUpperInvariant(c);
                        newWord = false;
                    }
                    else
                    {
                        span[pos++] = char.ToLowerInvariant(c);
                    }
                }
            }
        });
    }
}
