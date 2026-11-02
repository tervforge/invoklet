namespace Invoklet.Services.Preferences;

public sealed class LayoutSettings
{
    public double SidebarWidth { get; set; } = 300;
    public bool SidebarCollapsed { get; set; }
    public double DebugPanelHeight { get; set; } = 220;
    public bool DebugPanelCollapsed { get; set; }

    /// <summary>Main window client size when last closed in the normal state; null until then.</summary>
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }

    /// <summary>Folded sections of the sidebar: "Connections", "Scripts", "Samples". Samples starts folded.</summary>
    public List<string> CollapsedSidebarSections { get; set; } = ["Samples"];

    /// <summary>The share of the sidebar's height each open section has, by section name; a section not listed counts 1 (equal shares).</summary>
    public Dictionary<string, double>? SidebarSectionWeights { get; set; }

    /// <summary>IDs of folded connection groups in the sidebar's Connections tree.</summary>
    public List<string> CollapsedConnectionGroups { get; set; } = [];

    /// <summary>Paths of folded folders in the sidebar's Samples tree.</summary>
    public List<string> CollapsedSampleFolders { get; set; } = [];
}
