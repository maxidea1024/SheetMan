using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SheetMan.Extensions
{
    public enum NameCase
    {
        None,
        Camel,
        Pascal,
        Kebab,
        Snake,
        Train,
        Sentence
    }

    public static class StringExtensions
    {
        public static string SafeTrim(this string s)
        {
            return s != null ? s.Trim() : "";
        }

        //https://stackoverflow.com/questions/1904252/is-there-a-method-in-c-sharp-to-check-if-a-string-is-a-valid-identifier
        public static bool IsValidIdentifier(this string identifier)
        {
            if (String.IsNullOrEmpty(identifier))
                return false;

            // definition of a valid C# identifier: http://msdn.microsoft.com/en-us/library/aa664670(v=vs.71).aspx
            const string formattingCharacter = @"\p{Cf}";
            const string connectingCharacter = @"\p{Pc}";
            const string decimalDigitCharacter = @"\p{Nd}";
            const string combiningCharacter = @"\p{Mn}|\p{Mc}";
            const string letterCharacter = @"\p{Lu}|\p{Ll}|\p{Lt}|\p{Lm}|\p{Lo}|\p{Nl}";
            const string identifierPartCharacter = letterCharacter + "|" +
                                                   decimalDigitCharacter + "|" +
                                                   connectingCharacter + "|" +
                                                   combiningCharacter + "|" +
                                                   formattingCharacter;
            const string identifierPartCharacters = "(" + identifierPartCharacter + ")+";
            const string identifierStartCharacter = "(" + letterCharacter + "|_)";
            const string identifierOrKeyword = identifierStartCharacter + "(" +
                                               identifierPartCharacters + ")*";
            var validIdentifierRegex = new Regex("^" + identifierOrKeyword + "$", RegexOptions.Compiled);
            var normalizedIdentifier = identifier.Normalize();

            // Check that the identifier match the validIdentifer regex.
            return validIdentifierRegex.IsMatch(normalizedIdentifier);
        }


        #region Case conversion

        public static string ToCase(this string source, NameCase targetNameCase)
        {
            switch (targetNameCase)
            {
                case NameCase.Camel:
                    return ToCamelCase(source);
                case NameCase.Pascal:
                    return ToPascalCase(source);
                case NameCase.Kebab:
                    return ToKebabCase(source);
                case NameCase.Snake:
                    return ToSnakeCase(source);
                case NameCase.Train:
                    return ToTrainCase(source);
                case NameCase.Sentence:
                    return ToSentenceCase(source);
            }

            return source;
        }

        public static string ToCamelCase(this string source)
        {
            //if (source == null)
            //    throw new ArgumentNullException(nameof(source));
            if (source == null)
                return null;

            return SymbolsPipe(
                source,
                '\0',
                (s, disableFrontDelimeter) =>
                {
                    if (disableFrontDelimeter)
                        return new char[] { char.ToLowerInvariant(s) };

                    return new char[] { char.ToUpperInvariant(s) };
                });
        }

        public static string ToPascalCase(this string source)
        {
            //if (source == null)
            //    throw new ArgumentNullException(nameof(source));
            if (source == null)
                return null;

            return SymbolsPipe(
                source,
                '\0',
                (s, i) => new char[] { char.ToUpperInvariant(s) });
        }

        public static string ToKebabCase(this string source)
        {
            //if (source == null)
            //    throw new ArgumentNullException(nameof(source));
            if (source == null)
                return null;

            return SymbolsPipe(
                source,
                '-',
                (s, disableFrontDelimeter) =>
                {
                    if (disableFrontDelimeter)
                        return new char[] { char.ToLowerInvariant(s) };

                    return new char[] { '-', char.ToLowerInvariant(s) };
                });
        }

        public static string ToSnakeCase(this string source)
        {
            //if (source == null)
            //    throw new ArgumentNullException(nameof(source));
            if (source == null)
                return null;

            return SymbolsPipe(
                source,
                '_',
                (s, disableFrontDelimeter) =>
                {
                    if (disableFrontDelimeter)
                        return new char[] { char.ToLowerInvariant(s) };

                    return new char[] { '_', char.ToLowerInvariant(s) };
                });
        }

        public static string ToTrainCase(this string source)
        {
            //if (source == null)
            //    throw new ArgumentNullException(nameof(source));
            if (source == null)
                return null;

            return SymbolsPipe(
                source,
                '-',
                (s, disableFrontDelimeter) =>
                {
                    if (disableFrontDelimeter)
                        return new char[] { char.ToUpperInvariant(s) };

                    return new char[] { '-', char.ToUpperInvariant(s) };
                });
        }

        public static string ToSentenceCase(this string source)
        {
            if (string.IsNullOrEmpty(source))
                return source;

            string result = "";

            bool needToUpper = false;
            for (int i = 0; i < source.Length; i++)
            {
                if (i == 0)
                {
                    result += char.ToUpper(source[i]);
                }
                else if (source[i] == '_')
                {
                    needToUpper = true;
                    result += " ";
                }
                else
                {
                    if (needToUpper)
                    {
                        needToUpper = false;
                        result += char.ToUpper(source[i]);
                    }
                    else
                    {
                        result += source[i];
                    }
                }
            }

            return result;
        }


        private static readonly char[] Delimeters = { ' ', '-', '_' };

        private static bool IsFullyUppercasedOrUnderscoresOnly(string source)
        {
            //TODO 알파벳이 두글자 이상 연속으로 있는 경우에만..?
            //이거 판단하기가 좀 애매한데?
            foreach (var c in source)
            {
                if (!(c == '_' || char.IsUpper(c)))
                    return false;
            }

            return true;
        }

        private static string SymbolsPipe(string source, char mainDelimeter, Func<char, bool, char[]> newWordSymbolHandler)
        {
            if (string.IsNullOrEmpty(source))
                return source;

            // 모두 대문자이거나 "_"인 경우에는 변환을 거치지 않고 그냥 반환함.
            // 모든 케이스 타입에 해당하는지는 생각을 해봐야..
            //if (IsFullyUppercasedOrUnderscoresOnly(source))
            //    return source;

            // 앞쪽, 뒷쪽 "_" 문자들은 유지해주자.
            string headUnderscores = "";
            string tailUnderscores = "";

            int headUnderscoreCount = 0;
            int tailUnderscoreCount = 0;

            for (int i = 0; i < source.Length; i++)
            {
                if (source[i] == '_')
                    headUnderscoreCount++;
                else
                    break;
            }

            if (headUnderscoreCount > 0)
            {
                headUnderscores = source.Substring(0, headUnderscoreCount);
                source = source.Substring(headUnderscoreCount);

            }

            for (int i = source.Length-1; i >= 0; i--)
            {
                if (source[i] == '_')
                    tailUnderscoreCount++;
                else
                    break;
            }

            if (tailUnderscoreCount > 0)
            {
                tailUnderscores = source.Substring(source.Length - tailUnderscoreCount);
                source = source.Substring(0, source.Length - tailUnderscoreCount);
            }


            var builder = new StringBuilder();

            bool nextSymbolStartsNewWord = true;
            bool disableFrontDelimeter = true;
            for (var i = 0; i < source.Length; i++)
            {
                var symbol = source[i];
                if (Delimeters.Contains(symbol))
                {
                    if (symbol == mainDelimeter)
                    {
                        builder.Append(symbol);
                        disableFrontDelimeter = true;
                    }

                    nextSymbolStartsNewWord = true;
                }
                else if (!char.IsLetterOrDigit(symbol))
                {
                    builder.Append(symbol);
                    disableFrontDelimeter = true;
                    nextSymbolStartsNewWord = true;
                }
                else
                {
                    if (nextSymbolStartsNewWord || char.IsUpper(symbol))
                    {
                        builder.Append(newWordSymbolHandler(symbol, disableFrontDelimeter));
                        disableFrontDelimeter = false;
                        nextSymbolStartsNewWord = false;
                    }
                    else
                    {
                        builder.Append(symbol);
                    }
                }
            }

            return headUnderscores + builder.ToString() + tailUnderscores;
        }

        #endregion
    }
}
