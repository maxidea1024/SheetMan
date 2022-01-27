namespace SheetMan.CodeGeneration
{
    // typescript의 경우에는 camel case를 기본적으로 선호하므로
    // 키워드들로 이름을 지었을 경우에는 문제가 될수 있음.

    public partial class TsCodeGenerator
    {
        // https://github.com/microsoft/TypeScript/issues/2536
        static readonly string[] TypescriptKeywords = {
            "abstract", "await", "boolean", "break", "byte", "case", "catch", "char", "class", "const", "continue", "debugger",
            "default", "delete", "do", "double", "else", "enum", "export", "extends", "false", "final", "finally", "float",
            "for", "function", "goto", "if", "implements", "import", "in", "instanceof", "int", "interface", "let", "long",
            "native", "new", "null", "package", "private", "protected", "public", "return", "short", "static", "super",
            "switch", "synchronized", "this", "throw", "transient", "true", "try", "typeof", "var", "void", "volatile", "while",
            "with", "yield"
        };
    }
}
