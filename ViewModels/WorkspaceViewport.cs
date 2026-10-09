using System;
using System.Collections.Generic;

namespace CxShell.ViewModels;

/// <summary>The docked surfaces that compete with the terminal for width.</summary>
internal enum WorkspacePanel
{
    Agent,
    Monitor,
    Sftp,
    SessionSidebar
}

/// <summary>
/// Decides which docked panels give way when the window cannot hold them all. The
/// rail plus every panel is wider than the default window, and without a rule the
/// terminal column - the one surface that has to stay readable - is what collapses.
/// </summary>
internal static class WorkspaceViewport
{
    /// <summary>
    /// Order panels are closed in: the Agent chat gives way before the monitor, the
    /// monitor before the file browser, and the session tree last, because a session
    /// you cannot find is worse than a chart you cannot see.
    /// </summary>
    internal static readonly WorkspacePanel[] SacrificeOrder =
    [
        WorkspacePanel.Agent,
        WorkspacePanel.Monitor,
        WorkspacePanel.Sftp,
        WorkspacePanel.SessionSidebar
    ];

    /// <summary>
    /// Panels to close so the terminal keeps at least
    /// <paramref name="minimumTerminalWidth"/>. <paramref name="justOpened"/> is
    /// never returned: a button the user pressed must not appear to do nothing, so
    /// when even an empty workspace cannot satisfy the minimum the request wins and
    /// the terminal simply gets narrow.
    /// </summary>
    internal static IReadOnlyList<WorkspacePanel> FindPanelsToClose(
        double workspaceWidth,
        double navRailWidth,
        double minimumTerminalWidth,
        IReadOnlyDictionary<WorkspacePanel, double> openPanelWidths,
        WorkspacePanel? justOpened)
    {
        var used = navRailWidth;
        foreach (var width in openPanelWidths.Values)
            used += width;

        var toClose = new List<WorkspacePanel>();
        foreach (var panel in SacrificeOrder)
        {
            if (workspaceWidth - used >= minimumTerminalWidth)
                break;

            if (panel == justOpened || !openPanelWidths.ContainsKey(panel))
                continue;

            toClose.Add(panel);
            used -= openPanelWidths[panel];
        }

        return toClose;
    }
}
