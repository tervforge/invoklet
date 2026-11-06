using Invoklet.Format.MdRun;
using Invoklet.Windows.Models;

namespace Invoklet.NonUI.Tests.App.Windows.Models.Fixtures;

// A script that declares one argument and has one set of values, loaded into the Arguments tab with that set chosen.
internal sealed class ScriptArgumentsViewModelTypingFixture
{
    public MdRunFrontMatter Script { get; } = new()
    {
        Arguments = [new() { Name = "count", Type = MdRunArgumentType.Int }],
        ArgumentSets = new(StringComparer.Ordinal) { ["smoke"] = new(StringComparer.Ordinal) { ["count"] = MdRunValue.Of("3") } },
    };

    public ScriptArgumentsViewModel CreateSut()
    {
        var sut = new ScriptArgumentsViewModel();
        sut.Load(Script);
        sut.SelectedSet = sut.Sets[0];
        return sut;
    }
}
