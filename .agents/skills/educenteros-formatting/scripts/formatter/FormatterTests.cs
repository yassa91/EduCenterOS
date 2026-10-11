namespace EduCenterOS.Formatting;

internal static class FormatterTests
{
    internal static void Run()
    {
        var source = """
            class Example
            {
                private Example() { }
                public string Text { get; } = "a; b;";
                internal static Error Rejected => new("Example.Rejected", ErrorCategory.BusinessRule, "Safe message.");
                [First] [Second]
                void Run()
                {
                    var first = 1; var second = 2;
                    for (var i = 0; i < 3; i++) first++;
                    if (second > 1) return;
                }
            }
            """;
        var formatted = CodeFormatter.Format(source);
        Require(formatted.Contains("new(\n        \"Example.Rejected\",\n        ErrorCategory.BusinessRule,\n        \"Safe message.\"\n    )", StringComparison.Ordinal));
        Require(formatted.Contains("var first = 1;\n        var second = 2;", StringComparison.Ordinal));
        Require(formatted.Contains("[First]\n    [Second]", StringComparison.Ordinal));
        Require(formatted.Contains("public string Text { get; } = \"a; b;\";", StringComparison.Ordinal));
        Require(formatted.Contains("for (var i = 0; i < 3; i++) first++;", StringComparison.Ordinal));
        Require(formatted.Contains("if (second > 1) return;", StringComparison.Ordinal));
        Require(CodeFormatter.Format(formatted) == formatted);

        var condition = "class Example\n{\n    void Run(bool first, bool second)\n    {\n        if (first &&\n            second) return;\n    }\n}\n";
        var wrapped = CodeFormatter.Format(condition);
        Require(wrapped.Contains("if (\n            first &&\n            second\n        ) return;", StringComparison.Ordinal));
        Require(CodeFormatter.Format(wrapped) == wrapped);

        var literal = "class Example\n{\n    string Text = \"\"\"\n        private data;   \n        \"\"\";\n    // Keep this comment.\n    void Run() { return; }\n}\n";
        var preserved = CodeFormatter.Format(literal);
        Require(preserved.Contains("private data;   \n", StringComparison.Ordinal));
        Require(preserved.Contains("// Keep this comment.", StringComparison.Ordinal));
        Require(CodeFormatter.Format(preserved) == preserved);

        var commentedArguments = "class Example\n{\n    Error Failure() => new Error(\"Example.Rejected\", /* category rationale */ ErrorCategory.Conflict, \"Safe\");\n}\n";
        Require(CodeFormatter.Format(commentedArguments).Contains("/* category rationale */", StringComparison.Ordinal));

        var nested = CodeFormatter.Format("class Example { void Run() { if (true) { First(); Second(); } } }");
        Require(CodeFormatter.Format(nested) == nested);

        var unrelated = "class Example\n{\n    object Create() => new Thing(1, 2, 3);\n}\n";
        Require(CodeFormatter.Format(unrelated) == unrelated);

        try
        {
            CodeFormatter.Format("class {");
        }
        catch (InvalidOperationException exception) when (exception.Message == "Formatting.InvalidSyntax")
        {
            Console.WriteLine("Formatter tests: passed (layout, idempotence, loops, guards, literals, comments, nested blocks, unrelated calls, invalid syntax).");
            return;
        }

        throw new InvalidOperationException("Formatting.Tests.SyntaxFailureMissing");
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Formatting.Tests.Failed");
    }
}
