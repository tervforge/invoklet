using Invoklet.Languages.CSharp;
using Invoklet.Languages.CSharp.IntelliSense;
using Invoklet.Languages.CSharp.IntelliSense.ScriptDocument;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Invoklet.NonUI.Tests.Languages.CSharp.IntelliSense.Fixtures;

// The language service of C# is Roslyn through and through: it is not faked but made once per test, over the default references of
// the SDK and Invoklet's own (the same ones the editor gives it), and disposed by the test. The fixture owns how it is made, so the
// tests say what they do with it and not how a service is set up.
internal sealed class CSharpIntelliSenseFixture
{
    // The list of the default references is read from disk by the reference service; it does not change within a run.
    private static readonly Lazy<IReadOnlyList<string>> DefaultReferences = new(() =>
    {
        var references = new CSharpReferenceService();
        return [.. references.GetDefault().Append(references.GetExtensionReference())];
    });

    // What the service is made with, and so the framework a package update leaves in place.
    public static IReadOnlyList<string> Framework => ReferenceSet.Classify(DefaultReferences.Value, null).DefaultFramework;

    // Without a resolver of its own, a package update answers with the framework and nothing else, and looks at nothing on the disk.
    public CSharpIntelliSenseService CreateSut(ILogger<CSharpIntelliSenseService>? logger = null, IReferenceResolver? resolver = null)
        => new(DefaultReferences.Value, logger ?? NullLogger<CSharpIntelliSenseService>.Instance, null, resolver ?? new FixedReferenceResolver(Framework));
}
