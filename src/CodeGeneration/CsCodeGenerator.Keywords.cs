namespace SheetMan.CodeGeneration
{
    //코드 출력시에는 escaping을 하므로 문제가 되지는 않음.
    //  "class"라는 이름을 썼을 경우에 "_class" 또는 "Class" 로 바껴서 출력되므로 문제는 없음.

    public partial class CsCodeGenerator
    {
        // https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/
        private static readonly string[] CSharpKeywords = {
            /*

    "abstract","event","new","struct","as","explicit","null","switch","base","extern",
    "this","false","operator","throw","break","finally","out","true",
    "fixed","override","try","case","params","typeof","catch","for",
    "private","foreach","protected","checked","goto","public",
    "unchecked","class","if","readonly","unsafe","const","implicit","ref",
    "continue","in","return","using","virtual","default",
    "interface","sealed","volatile","delegate","internal","do","is",
    "sizeof","while","lock","stackalloc","else","static","enum",
    "namespace",
    "object","bool","byte","float","uint","char","ulong","ushort",
    "decimal","int","sbyte","short","double","long","string","void",
    "partial", "yield", "where"

            */

            // keywords
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const", "continue", "decimal",
            "default", "delegate", "do", "double", "else", "enum",
            "event", "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int",
            "interface", "internal", "is", "lock", "long",

            "namespace", "new", "null", "object", "operator", "out", "override", "params", "private", "protected", "public", "readonly",
            "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc",

            "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe",
            "ushort", "using", "virtual", "void", "volatile", "while",

            // contextual keywords
            "add", "and", "alias", "ascending", "async", "await", "by", "descending", "dynamic", "equals", "from",
            "get", "global", "group", "init", "into", "join", "let", "managed", "nameof", "nint", "not", "notnull",
            "nuint", "on", "or", "orderby", "partial", "record", "remove", "select", "set",
            "unmanaged", "value", "var", "when", "where", "with", "yield"
        };
    }
}
