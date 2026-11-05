using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Invoklet.Languages.CSharp.IntelliSense.Analysis;

/// <summary>
/// Warns when the value of a secret (<c>Util.GetSecret</c>) is written to the output: by <c>.Dump()</c> or <c>Console.Write</c>/<c>WriteLine</c>.
/// What a run writes is shown in the results and kept with the script's build, so the secret would leave the Secret Store. The value is
/// followed within one method (top-level statements included) through locals, concatenation, interpolation, <c>?:</c>, <c>??</c> and
/// <c>params</c> arrays; what is computed from it (its length, a hash) is not the secret and is not followed.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class SecretOutputAnalyzer : DiagnosticAnalyzer
{
    public const string Id = "INV0001";

    /// <summary>The diagnostic's properties: the sink (<see cref="DumpSink"/> or <see cref="ConsoleSink"/>) and the secret's name.</summary>
    public const string SinkProperty = "Sink", SecretProperty = "Secret";
    public const string DumpSink = "Dump", ConsoleSink = "Console";

    private static readonly DiagnosticDescriptor Rule = new(
        Id,
        "A secret is written to the output",
        "The secret '{0}' is written to the output by {1}: it shows in the results and is saved with the run's output",
        "Security",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationBlockAction(Analyze);
    }

    private static void Analyze(OperationBlockAnalysisContext context)
    {
        var operations = context.OperationBlocks.SelectMany(block => block.DescendantsAndSelf()).ToList();

        // Locals holding a secret, by the secret's name; repeated until nothing changes, so `var b = a;` after `var a = GetSecret(..)` counts.
        var secretLocals = new Dictionary<ILocalSymbol, string>(SymbolEqualityComparer.Default);
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var operation in operations)
            {
                var (local, value) = operation switch
                {
                    IVariableDeclaratorOperation { Initializer.Value: var initial } declarator => (declarator.Symbol, initial),
                    ISimpleAssignmentOperation { Target: ILocalReferenceOperation target } assignment => (target.Local, assignment.Value),
                    _ => (null, null),
                };
                if (local is null || secretLocals.ContainsKey(local) || SecretIn(value, secretLocals) is not { } name) continue;
                secretLocals[local] = name;
                changed = true;
            }
        }

        foreach (var invocation in operations.OfType<IInvocationOperation>())
        {
            var sink = IsDump(invocation) ? DumpSink : IsConsoleWrite(invocation) ? ConsoleSink : null;
            if (sink is null) continue;
            // Dump writes only its receiver; its other arguments are a label and options.
            var written = sink == DumpSink ? invocation.Arguments.Take(1) : invocation.Arguments;
            if (written.Select(argument => SecretIn(argument.Value, secretLocals)).FirstOrDefault(name => name is not null) is not { } secret) continue;
            context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Syntax.GetLocation(),
                ImmutableDictionary<string, string?>.Empty.Add(SinkProperty, sink).Add(SecretProperty, secret),
                secret, sink == DumpSink ? ".Dump()" : "Console." + invocation.TargetMethod.Name));
        }
    }

    // The name of the secret this value is, or null when it is none.
    private static string? SecretIn(IOperation? operation, IReadOnlyDictionary<ILocalSymbol, string> secretLocals) => operation switch
    {
        null => null,
        IInvocationOperation invocation when IsGetSecret(invocation) =>
            invocation.Arguments.FirstOrDefault()?.Value.ConstantValue is { HasValue: true, Value: string name } ? name : "(computed name)",
        // Dump returns what it was given.
        IInvocationOperation invocation when IsDump(invocation) => SecretIn(invocation.Arguments[0].Value, secretLocals),
        ILocalReferenceOperation reference => secretLocals.TryGetValue(reference.Local, out var name) ? name : null,
        IConversionOperation conversion => SecretIn(conversion.Operand, secretLocals),
        IParenthesizedOperation parenthesized => SecretIn(parenthesized.Operand, secretLocals),
        IArgumentOperation argument => SecretIn(argument.Value, secretLocals),
        IInterpolatedStringOperation interpolated => FirstSecret(
            interpolated.Parts.OfType<IInterpolationOperation>().Select(part => part.Expression), secretLocals),
        IBinaryOperation { OperatorKind: BinaryOperatorKind.Add, Type.SpecialType: SpecialType.System_String } concatenation =>
            SecretIn(concatenation.LeftOperand, secretLocals) ?? SecretIn(concatenation.RightOperand, secretLocals),
        IConditionalOperation conditional => SecretIn(conditional.WhenTrue, secretLocals) ?? SecretIn(conditional.WhenFalse, secretLocals),
        ICoalesceOperation coalesce => SecretIn(coalesce.Value, secretLocals) ?? SecretIn(coalesce.WhenNull, secretLocals),
        IArrayCreationOperation array => FirstSecret(array.Initializer?.ElementValues ?? [], secretLocals),
        _ => null,
    };

    private static string? FirstSecret(IEnumerable<IOperation> operations, IReadOnlyDictionary<ILocalSymbol, string> secretLocals)
        => operations.Select(operation => SecretIn(operation, secretLocals)).FirstOrDefault(name => name is not null);

    // Invoklet.Runtime's Util and ObjectExtensions are in the global namespace.
    private static bool IsGetSecret(IInvocationOperation invocation)
        => invocation.TargetMethod is { Name: "GetSecret", ContainingType: { Name: "Util", ContainingNamespace.IsGlobalNamespace: true } };

    private static bool IsDump(IInvocationOperation invocation)
        => invocation.TargetMethod is { Name: "Dump", ContainingType: { Name: "ObjectExtensions", ContainingNamespace.IsGlobalNamespace: true } }
           && invocation.Arguments.Length > 0;

    private static bool IsConsoleWrite(IInvocationOperation invocation)
        => invocation.TargetMethod is { Name: "Write" or "WriteLine", ContainingType: { Name: "Console", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } } };
}
