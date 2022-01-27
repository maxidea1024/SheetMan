using System;
using System.Text;
using System.Collections.Generic;
using System.IO;

namespace SheetMan
{
    public class Printer
    {
        public string LinefeedStr = "\r\n";
        public string TabStr = "    ";

        private Dictionary<string, string> _globalVars = new Dictionary<string, string>();
        private readonly List<Dictionary<string, string>> _scopedVarsStack = new List<Dictionary<string, string>>();

        private string _indentStr = "";
        private bool _startOfLine = true;
        private bool _suppressIndentification = false;

        private readonly StringBuilder _sb = new StringBuilder();

        public Printer()
        {
        }

        public Printer(string[] keyAndValues)
        {
            SetGlobalVars(keyAndValues);
        }

        public Printer(Dictionary<string, string> vars)
        {
            SetGlobalVars(vars);
        }

        public void SetGlobalVars(string[] keyAndValues)
        {
            _globalVars = KeyAndValuesToVars(keyAndValues);
        }

        public void SetGlobalVars(Dictionary<string, string> vars)
        {
            _globalVars = vars;
        }

        public void PushScopedVars(string[] keyAndValues)
        {
            var vars = KeyAndValuesToVars(keyAndValues);
            PushScopedVars(vars);
        }

        private Dictionary<string, string> KeyAndValuesToVars(string[] keyAndValues)
        {
            if ((keyAndValues.Length % 2) != 0)
                throw new ArgumentException();

            var vars = new Dictionary<string, string>();
            for (int i = 0; i < keyAndValues.Length; i += 2)
                vars.Add(keyAndValues[i + 0], keyAndValues[i + 1]);

            return vars;
        }

        public void PushScopedVars(Dictionary<string, string> vars)
        {
            _scopedVarsStack.Add(vars);
        }

        public void PopScopedVars()
        {
            _scopedVarsStack.RemoveAt(_scopedVarsStack.Count - 1);
        }

        public string ToMappedString(string str)
        {
            int variableDelimiterCount = 0;
            for (int i = 0; i < str.Length; ++i)
            {
                if (str[i] == '$')
                {
                    variableDelimiterCount++;
                    break;
                }
            }

            if (variableDelimiterCount == 0)
                return str;

            var mappedStr = new StringBuilder();

            int length = str.Length;
            int pos = 0;
            for (int i = 0; i < length; ++i)
            {
                if (str[i] == '$' && (i < length - 1 && !char.IsDigit(str[i + 1])))
                {
                    mappedStr.Append(str.Substring(pos, i - pos));
                    pos = i + 1;

                    int endDelimiterPos = str.IndexOf('$', pos);
                    if (endDelimiterPos < 0)
                        throw new ArgumentException($"unclosed variable name. at {pos}");

                    string varName = str.Substring(pos, endDelimiterPos - pos);
                    if (varName.Length == 0)
                    {
                        mappedStr.Append("$");
                    }
                    else
                    {
                        if (FindVariable(varName, out string value))
                        {
                            // recursive..
                            //여기서 문제가 발생할 수 있음.
                            value = ToMappedString(value);

                            mappedStr.Append(value);
                        }
                        else
                        {
                            throw new ArgumentException($"undefined variable name: {varName}");
                        }
                    }

                    i = endDelimiterPos;
                    pos = endDelimiterPos + 1;
                }
                else
                {
                    string span = str.Substring(pos, i - pos + 1);
                    mappedStr.Append(span);
                    pos = i + 1;
                }
            }

            return mappedStr.ToString();
        }

        private bool FindVariable(string varName, out string outValue)
        {
            outValue = "";

            for (int i = _scopedVarsStack.Count - 1; i >= 0; --i)
            {
                var vars = _scopedVarsStack[i];
                if (vars.TryGetValue(varName, out outValue))
                    return true;
            }

            // Fallback.
            if (_globalVars != null)
            {
                if (_globalVars.TryGetValue(varName, out outValue))
                    return true;
            }

            return false;
        }

        public void PrintLineWithoutIndents(string str)
        {
            _suppressIndentification = true;
            PrintLine(str);
            _suppressIndentification = false;
        }


        public void PrintLines(int n)
        {
            for (int i = 0; i < n; i++)
                PrintLine();
        }

        public void PrintLine()
        {
            Print(LinefeedStr);
        }

        public void PrintLine(string str)
        {
            Print(str);
            Print(LinefeedStr);
        }

        public void PrintLine(string str, string var1, string value1, string var2, string value2, string var3, string value3, string var4, string value4)
        {
            Print(str, var1, value1, var2, value2, var3, value3, var4, value4);
            Print(LinefeedStr);
        }

        public void PrintLine(string str, string var1, string value1, string var2, string value2, string var3, string value3)
        {
            Print(str, var1, value1, var2, value2, var3, value3);
            Print(LinefeedStr);
        }

        public void PrintLine(string str, string var1, string value1, string var2, string value2)
        {
            Print(str, var1, value1, var2, value2);
            Print(LinefeedStr);
        }

        public void PrintLine(string str, string var1, string value1)
        {
            Print(str, var1, value1);
            Print(LinefeedStr);
        }

        public void PrintLine(string str, string[] keyAndValues)
        {
            Print(str, keyAndValues);
            Print(LinefeedStr);
        }

        public void PrintLine(string str, Dictionary<string, string> vars)
        {
            Print(str, vars);
            Print(LinefeedStr);
        }

        public void Printf(string str, params object[] args)
        {
            Print(string.Format(str, args));
        }

        public void PrintLinef(string str, params object[] args)
        {
            PrintLine(string.Format(str, args));
        }

        public void Print(string str)
        {
            string mappedStr = ToMappedString(str);

            int length = mappedStr.Length;
            int pos = 0;

            for (int i = 0; i < length; ++i)
            {
                if (mappedStr[i] == '\n')
                {
                    PrintRaw(mappedStr.Substring(pos, i - pos + 1));
                    pos = i + 1;
                    _startOfLine = true;
                }
                else
                {
                    PrintRaw(mappedStr.Substring(pos, i - pos + 1));
                    pos = i + 1;
                }
            }
        }

        public void Print(string str, string var1, string value1, string var2, string value2, string var3, string value3, string var4, string value4)
        {
            Print(str, new string[] { var1, value1, var2, value2, var3, value3, var4, value4 });
        }

        public void Print(string str, string var1, string value1, string var2, string value2, string var3, string value3)
        {
            Print(str, new string[] { var1, value1, var2, value2, var3, value3 });
        }

        public void Print(string str, string var1, string value1, string var2, string value2)
        {
            Print(str, new string[] { var1, value1, var2, value2 });
        }

        public void Print(string str, string var1, string value1)
        {
            Print(str, new string[] { var1, value1 });
        }

        public void Print(string str, string[] keyAndValues)
        {
            PushScopedVars(keyAndValues);
            Print(str);
            PopScopedVars();
        }

        public void Print(string str, Dictionary<string, string> vars)
        {
            PushScopedVars(vars);
            Print(str);
            PopScopedVars();
        }

        public void PrintRaw(string str)
        {
            if (str.Length == 0)
                return;

            if (_startOfLine && str.Length > 0 && str[0] != '\n')
            {
                bool printIndents = true;

                if (_suppressIndentification)
                    printIndents = false;

                //FIXME 이게 의도대로 동작을 안하네...
                if (str.StartsWith("<<<<"))
                {
                    str = str.Substring(4);
                    printIndents = false;
                }

                _startOfLine = false;

                if (printIndents)
                    PrintRaw(_indentStr);
            }

            _sb.Append(str);
        }

        public void Indent(int count = 1)
        {
            for (int i = 0; i < count; ++i)
                _indentStr += TabStr;
        }

        public void Outdent(int count = 1)
        {
            if (TabStr.Length == 0)
                return;

            if (_indentStr.Length < (TabStr.Length * count))
                throw new Exception("Outdent() without matching Indent().");

            int oldCount = _indentStr.Length / TabStr.Length;
            int newCount = oldCount - count;

            _indentStr = "";
            for (int i = 0; i < newCount; ++i)
                _indentStr += TabStr;
        }

        public void ScopeIn(string str)
        {
            if (str.Length > 0)
            {
                Print(str);
                Print(LinefeedStr);
            }

            Indent(1);
        }

        public void ScopeOut(string str)
        {
            Outdent(1);

            if (str.Length > 0)
            {
                Print(str);
                Print(LinefeedStr);
            }
        }

        public override string ToString()
        {
            var str = _sb.ToString();

            // Normalize linefeed.
            str = str.Replace("\r\n", "\n");

            //줄별로 분리해서 rtrim을 해주어서 정규화함.
            var lines = str.Split('\n');

            // 재조립
            var sb2 = new StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                sb2.Append(lines[i].TrimEnd().Replace("\t", TabStr));
                sb2.Append("\n");
            }

            return sb2.ToString();
        }

        public void WriteToFile(string filename)
        {
            string contents = ToString();
            File.WriteAllText(filename, contents);
        }
    }
}
