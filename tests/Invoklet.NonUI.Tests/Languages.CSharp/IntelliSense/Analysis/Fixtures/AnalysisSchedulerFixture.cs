using Invoklet.Languages.CSharp.IntelliSense.Analysis;

namespace Invoklet.NonUI.Tests.Languages.CSharp.IntelliSense.Analysis.Fixtures;

// AnalysisScheduler has no injected objects, only delegates, which the fixture owns: the debounce, how a pass is started (on the
// caller's thread: it runs until its first wait, and the rest comes back as the awaited work completes) and what is told of a failure.
internal sealed class AnalysisSchedulerFixture
{
    public TimeSpan Debounce { get; set; } = TimeSpan.Zero;

    public List<Exception> Failures { get; } = [];

    public TimeSpan StopTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public AnalysisScheduler CreateSut()
        => new(() => Debounce, work => work(), Failures.Add, StopTimeout);

    /// <summary>Waits for the pass the scheduler started last, however many it chained.</summary>
    public static async Task SettleAsync(AnalysisScheduler sut)
    {
        Task? seen = null;
        while (sut.Pass is { } pass && !ReferenceEquals(pass, seen))
        {
            seen = pass;
            await pass;
        }
    }
}
