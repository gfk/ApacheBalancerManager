namespace ApacheBalancerWasmInterface.Layout;

/// <summary>
/// Names of the layout sections pages can render into. Lets a page own the state behind
/// a control (refresh timer, busy flags) while the markup lands inside <c>MainLayout</c>'s header.
/// </summary>
public static class LayoutSections
{
    /// <summary>Toolbar controls shown at the left of the application header.</summary>
    public const string HeaderControls = "header-controls";
}
