using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Dynamic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace TinyCsvNet
{
    public struct CsvOption
    {
        public enum EOL
        {
            Unix,
            Machintosh,
            Windows
        }

        public char Delimiter;
        public EOL EOLType;
        public Encoding Encoding;

        public CsvOption(char delimiter, Encoding encoding, EOL eol)
        {
            Delimiter = delimiter;
            EOLType = eol;
            Encoding = encoding;
        }

        public static CsvOption Default => new CsvOption(',', Encoding.UTF8, EOL.Unix);
        public static CsvOption SJIS
        {
            get
            {
                setupEncoding();
                return new CsvOption(',', Encoding.GetEncoding(932), EOL.Windows);
            }
        }

        private static bool isLoadedEncodings;
        private static void setupEncoding()
        {
            if (!isLoadedEncodings)
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                isLoadedEncodings = true;
            }
        }
    }

    public class CsvParseError : Exception
    {

    }

    public class CsvReader
    {
        public static List<List<string>> ParseAsListAsync(Stream csv, CsvOption option)
        {
            using var reader = new StreamReader(csv, option.Encoding);
            return ParseAsListAsync(reader, option);
        }

        public static List<List<string>> ParseAsListAsync(StreamReader csv, CsvOption option)
        {
            Span<char> buf = stackalloc char[256];
            var parser = new ParserIntermediate(buf, option.Delimiter, option.EOLType);
            return parser.Parse(csv);
        }

        private ref struct ParserIntermediate
        {
            const char QUOTE = '"';
            const char CR = '\r';
            const char LF = '\n';

            Span<char> buffer;
            StringBuilder sb;
            char delimiter;
            CsvOption.EOL eol;

            bool inQuote;
            bool undeterminedQuote;
            bool undeterminedCR;

            public ParserIntermediate(Span<char> buf, char del, CsvOption.EOL eol)
            {
                this.buffer = buf;
                this.sb = new StringBuilder();
                this.delimiter = del;
                this.eol = eol;

                inQuote = false;
                undeterminedQuote = false;
                undeterminedCR = false;
            }

            private enum EndState
            {
                ColEnd,
                RowEnd,
                Undetermined
            }

            public List<List<string>> Parse(StreamReader stream)
            {
                var cols = new List<string>();
                var rows = new List<List<string>>();
                ReadOnlySpan<char> input = default;
                var state = EndState.Undetermined;

                while (true)
                {
                    if (input.IsEmpty)
                    {
                        input.CopyTo(buffer);
                        var len = stream.ReadBlock(buffer.Slice(input.Length));
                        input = len + input.Length < buffer.Length ? buffer.Slice(0, len + input.Length) : buffer;
                    }
                    state = parseCol(input, out input);
                    switch (state)
                    {
                        case EndState.ColEnd:
                            cols.Add(sb.ToString());
                            sb.Clear();
                            break;
                        case EndState.RowEnd:
                            cols.Add(sb.ToString());
                            sb.Clear();
                            rows.Add(cols);
                            if (stream.EndOfStream && input.IsEmpty)
                                return rows;
                            cols = new List<string>();
                            break;
                    }
                }
                throw new CsvParseError();
            }

            private EndState parseCol(ReadOnlySpan<char> input, out ReadOnlySpan<char> rest)
            {
                if (input.IsEmpty)
                {
                    if (undeterminedCR)
                        throw new CsvParseError();
                    if (inQuote)
                        throw new CsvParseError();

                    inQuote = false;
                    undeterminedQuote = false;
                    undeterminedCR = false;
                    rest = input;
                    return EndState.RowEnd;
                }

                if (inQuote)
                    return parseColWithQuote(input, out rest);

                if (input[0] == QUOTE)
                {
                    inQuote = true;
                    return parseColWithQuote(input.Slice(1), out rest);
                }
                    
                for (var i = 0; i < input.Length; i++)
                {
                    var c = input[i];
                    if (undeterminedCR)
                    {
                        if (c == LF)
                        {
                            inQuote = false;
                            undeterminedQuote = false;
                            undeterminedCR = false;
                            rest = i + 1 == input.Length ? default : input.Slice(i + 1);
                            sb.Append(input.Slice(0, i - 1));
                            return EndState.RowEnd;
                        }
                        undeterminedCR = false;
                    }
                    if (c == CR)
                    {
                        if (eol == CsvOption.EOL.Machintosh)
                        {
                            inQuote = false;
                            undeterminedQuote = false;
                            undeterminedCR = false;
                            rest = i + 1 == input.Length ? default : input.Slice(i + 1);
                            sb.Append(input.Slice(0, i));
                            return EndState.RowEnd;
                        }
                        else if (eol == CsvOption.EOL.Windows)
                        {
                            undeterminedCR = true;
                            continue;
                        }
                        else
                            continue;
                    }
                    else if (c == LF)
                    {
                        if (eol == CsvOption.EOL.Unix)
                        {
                            inQuote = false;
                            undeterminedQuote = false;
                            undeterminedCR = false;
                            rest = i + 1 == input.Length ? default : input.Slice(i + 1);
                            sb.Append(input.Slice(0, i));
                            return EndState.RowEnd;
                        }
                    }
                    else if (c == delimiter)
                    {
                        inQuote = false;
                        undeterminedQuote = false;
                        undeterminedCR = false;
                        rest = i + 1 == input.Length ? default : input.Slice(i + 1);
                        sb.Append(input.Slice(0, i));
                        return EndState.ColEnd;
                    }
                }
                sb.Append(input);
                rest = default;
                return EndState.Undetermined;
            }

            private EndState parseColWithQuote(ReadOnlySpan<char> input, out ReadOnlySpan<char> rest)
            {
                for (var i = 0; i < input.Length; i++)
                {
                    var c = input[i];
                    if (undeterminedCR)
                    {
                        if (c == LF)
                        {
                            inQuote = false;
                            undeterminedQuote = false;
                            undeterminedCR = false;
                            rest = i + 1 == input.Length ? default : input.Slice(i + 1);
                            sb.Append(input.Slice(0, i - 2));
                            return EndState.RowEnd;
                        }
                        else
                            throw new CsvParseError();
                    }
                    else if (undeterminedQuote)
                    {
                        if (c == QUOTE)
                        {
                            undeterminedQuote = false;
                            rest = i + 1 == input.Length ? default : input.Slice(i + 1);
                            sb.Append(input.Slice(0, i));
                            return EndState.Undetermined;
                        }
                        else if (c == delimiter)
                        {
                            inQuote = false;
                            undeterminedQuote = false;
                            undeterminedCR = false;
                            rest = i + 1 == input.Length ? default : input.Slice(i + 1);
                            sb.Append(input.Slice(0, i - 1));
                            return EndState.ColEnd;
                        }
                        else if (c == CR)
                        {
                            if (eol == CsvOption.EOL.Windows)
                            {
                                undeterminedCR = true;
                                undeterminedQuote = false;
                                continue;
                            }
                            else if (eol == CsvOption.EOL.Machintosh)
                            {
                                inQuote = false;
                                undeterminedQuote = false;
                                undeterminedCR = false;
                                rest = i + 1 == input.Length ? default : input.Slice(i + 1);
                                sb.Append(input.Slice(0, i - 1));
                                return EndState.RowEnd;
                            }
                            else
                                throw new CsvParseError();
                        }
                        else if (c == LF)
                        {
                            if (eol == CsvOption.EOL.Unix)
                            {
                                inQuote = false;
                                undeterminedQuote = false;
                                undeterminedCR = false;
                                rest = i + 1 == input.Length ? default : input.Slice(i + 1);
                                sb.Append(input.Slice(0, i - 1));
                                return EndState.RowEnd;
                            }
                            else
                                throw new CsvParseError();
                        }
                        throw new CsvParseError();
                    }
                    else if (c == QUOTE)
                    {
                        undeterminedQuote = true;
                        continue;
                    }
                }
                rest = default;
                sb.Append(input);
                return EndState.Undetermined;
            }
        }
    }
}
