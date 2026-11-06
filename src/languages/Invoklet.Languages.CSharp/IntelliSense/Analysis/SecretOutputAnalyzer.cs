using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Invoklet.Languages.CSharp.IntelliSense.Analysis;

/// <summary>
/// Warns when the value of a secret (<c>Util.GetSecret</c>, or a connection's property built from a <c>${secret:…}</c>, such as a database's
/// <c>ConnectionString</c>) is written to the output: by <c>.Dump()</c>, <c>Console.Write</c>/<c>WriteLine</c> (also through
/// <c>Console.Out</c>/<c>Console.Error</c>), <c>Debug</c> or <c>Trace</c>. What a run writes is shown in the results and kept with the
/// script's build, so the secret would leave the Secret Store. The value is followed within one method (top-level statements included)
/// through locals, concatenation, interpolation, <c>?:</c>, <c>??</c> and <c>params</c> arrays; what is computed from it (its length, a hash)
/// is not the secret and is not followed, nor is a secret passed to another method.
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
        "The secret '{0}' is written to the output by {1}",
        "Security",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var connectionSecrets = ConnectionSecrets(start.Compilation, start.CancellationToken);
            start.RegisterOperationBlockAction(block => Analyze(block, connectionSecrets));
        });
    }

    // The properties of the connections' classes (Connections.ShopClient and its bases) that the generated constructors set from a
    // secret, by the secret's name: a database's ConnectionString with a ${secret:…} in it.
    private static IReadOnlyDictionary<IPropertySymbol, string> ConnectionSecrets(Compilation compilation, CancellationToken cancellationToken)
    {
        var found = new Dictionary<IPropertySymbol, string>(SymbolEqualityComparer.Default);
        if (compilation.GlobalNamespace.GetTypeMembers("Connections").FirstOrDefault() is not { } connections) return found;
        foreach (var constructor in connections.GetTypeMembers().SelectMany(client => client.InstanceConstructors))
        {
            foreach (var reference in constructor.DeclaringSyntaxReferences)
            {
                // RS1030 guards against a model per tree on every analysis; here it is the few generated constructors of the connections,
                // once per compilation, and the only way to tie a property they set to its use in the script while the editor analyses
                // (it does not run compilation-end analysis in the background).
#pragma warning disable RS1030
                var model = compilation.GetSemanticModel(reference.SyntaxTree);
#pragma warning restore RS1030
                if (model.GetOperation(reference.GetSyntax(cancellationToken), cancellationToken) is not { } body) continue;
                var operations = body.DescendantsAndSelf().ToList();
                var locals = SecretLocals(operations, found);
                foreach (var assignment in operations.OfType<ISimpleAssignmentOperation>())
                    if (assignment.Target is IPropertyReferenceOperation property && SecretIn(assignment.Value, locals, found) is { } name)
                        found[property.Property.OriginalDefinition] = name;
            }
        }
        return found;
    }

    private static void Analyze(OperationBlockAnalysisContext context, IReadOnlyDictionary<IPropertySymbol, string> connectionSecrets)
    {
        var operations = context.OperationBlocks.SelectMany(block => block.DescendantsAndSelf()).ToList();
        var secretLocals = SecretLocals(operations, connectionSecrets);

        foreach (var invocation in operations.OfType<IInvocationOperation>())
        {
            var sink = IsDump(invocation) ? DumpSink : OutputName(invocation) is not null ? ConsoleSink : null;
            if (sink is null) continue;
            // Dump writes only its receiver; its other arguments are a label and options.
            var written = sink == DumpSink ? invocation.Arguments.Take(1) : invocation.Arguments;
            if (written.Select(argument => SecretIn(argument.Value, secretLocals, connectionSecrets)).FirstOrDefault(name => name is not null) is not { } secret)
                continue;
            context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Syntax.GetLocation(),
                ImmutableDictionary<string, string?>.Empty.Add(SinkProperty, sink).Add(SecretProperty, secret),
                secret, sink == DumpSink ? ".Dump()" : OutputName(invocation)));
        }
    }

    // Locals holding a secret, by the secret's name.
    private static Dictionary<ILocalSymbol, string> SecretLocals(IReadOnlyList<IOperation> operations, IReadOnlyDictionary<IPropertySymbol, string> connectionSecrets)
    {

        // Repeated until nothing changes, so `var b = a;` after `var a = GetSecret(..)` counts.
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
                if (local is null || secretLocals.ContainsKey(local) || SecretIn(value, secretLocals, connectionSecrets) is not { } name) continue;
                secretLocals[local] = name;
                changed = true;
            }
        }
        return secretLocals;
    }

    // The name of the secret this value is, or null when it is none.
    private static string? SecretIn(IOperation? operation, IReadOnlyDictionary<ILocalSymbol, string> secretLocals,
        IReadOnlyDictionary<IPropertySymbol, string> connectionSecrets) => operation switch
        {
            null => null,
            IInvocationOperation invocation when IsGetSecret(invocation) =>
                invocation.Arguments.FirstOrDefault()?.Value.ConstantValue is { HasValue: true, Value: string name } ? name : "(computed name)",
            // Dump returns what it was given.
            IInvocationOperation invocation when IsDump(invocation) => SecretIn(invocation.Arguments[0].Value, secretLocals, connectionSecrets),
            ILocalReferenceOperation reference => secretLocals.TryGetValue(reference.Local, out var name) ? name : null,
            IPropertyReferenceOperation property => connectionSecrets.TryGetValue(property.Property.OriginalDefinition, out var name) ? name : null,
            IConversionOperation conversion => SecretIn(conversion.Operand, secretLocals, connectionSecrets),
            IParenthesizedOperation parenthesized => SecretIn(parenthesized.Operand, secretLocals, connectionSecrets),
            IArgumentOperation argument => SecretIn(argument.Value, secretLocals, connectionSecrets),
            IInterpolatedStringOperation interpolated => FirstSecret(
                interpolated.Parts.OfType<IInterpolationOperation>().Select(part => part.Expression), secretLocals, connectionSecrets),
            IBinaryOperation { OperatorKind: BinaryOperatorKind.Add, Type.SpecialType: SpecialType.System_String } concatenation =>
                SecretIn(concatenation.LeftOperand, secretLocals, connectionSecrets) ?? SecretIn(concatenation.RightOperand, secretLocals, connectionSecrets),
            IConditionalOperation conditional => SecretIn(conditional.WhenTrue, secretLocals, connectionSecrets) ?? SecretIn(conditional.WhenFalse, secretLocals, connectionSecrets),
            ICoalesceOperation coalesce => SecretIn(coalesce.Value, secretLocals, connectionSecrets) ?? SecretIn(coalesce.WhenNull, secretLocals, connectionSecrets),
            IArrayCreationOperation array => FirstSecret(array.Initializer?.ElementValues ?? [], secretLocals, connectionSecrets),
            _ => null,
        };

    private static string? FirstSecret(IEnumerable<IOperation> operations, IReadOnlyDictionary<ILocalSymbol, string> secretLocals,
        IReadOnlyDictionary<IPropertySymbol, string> connectionSecrets)
        => operations.Select(operation => SecretIn(operation, secretLocals, connectionSecrets)).FirstOrDefault(name => name is not null);

    // Invoklet.Runtime's Util and ObjectExtensions are in the global namespace.
    private static bool IsGetSecret(IInvocationOperation invocation)
        => invocation.TargetMethod is { Name: "GetSecret", ContainingType: { Name: "Util", ContainingNamespace.IsGlobalNamespace: true } };

    private static bool IsDump(IInvocationOperation invocation)
        => invocation.TargetMethod is { Name: "Dump", ContainingType: { Name: "ObjectExtensions", ContainingNamespace.IsGlobalNamespace: true } }
           && invocation.Arguments.Length > 0;

    // How the message names an output call, or null for a call that writes nowhere the run's output goes: Console.Write/WriteLine,
    // Console.Out/Error.Write/WriteLine, Debug.Write…/Print and Trace.Write…/Trace…, the last two read by the debugger's output.
    private static string? OutputName(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod;
        if (method.Name is "Write" or "WriteLine" && IsSystemType(method.ContainingType, "Console")) return "Console." + method.Name;
        if (method.Name is "Write" or "WriteLine" && invocation.Instance is IPropertyReferenceOperation { Property: { Name: "Out" or "Error" } stream }
            && IsSystemType(stream.ContainingType, "Console"))
            return $"Console.{stream.Name}.{method.Name}";
        var diagnostics = method.ContainingType is { ContainingNamespace: { Name: "Diagnostics", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } } } type
            ? type.Name : null;
        if (diagnostics == "Debug" && method.Name is "Write" or "WriteLine" or "WriteIf" or "WriteLineIf" or "Print") return "Debug." + method.Name;
        if (diagnostics == "Trace" && method.Name is "Write" or "WriteLine" or "WriteIf" or "WriteLineIf" or "TraceInformation" or "TraceWarning" or "TraceError")
            return "Trace." + method.Name;
        return null;
    }

    private static bool IsSystemType(INamedTypeSymbol? type, string name)
        => type is { ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } } && type.Name == name;
}
