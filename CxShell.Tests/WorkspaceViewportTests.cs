using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using CxShell.ViewModels;

namespace CxShell.Tests;

public sealed class WorkspaceViewportTests
{
    private const double Rail = MainWindowViewModel.NavRailWidth;
    private const double Minimum = MainWindowViewModel.MinimumTerminalWidth;

    private static readonly Dictionary<WorkspacePanel, double> AllPanels = new()
    {
        [WorkspacePanel.SessionSidebar] = MainWindowViewModel.SessionSidebarWidth,
        [WorkspacePanel.Sftp] = MainWindowViewModel.DefaultSftpPanelWidth + 8,
        [WorkspacePanel.Monitor] = MainWindowViewModel.MonitorPanelWidth,
        [WorkspacePanel.Agent] = MainWindowViewModel.DefaultAgentPanelWidth + 1
    };

    [Fact]
    public void DefaultWindow_CannotFitEveryPanelSoTheTerminalSurvives()
    {
        // The regression this guards: rail + sidebar + SFTP + monitor + Agent is
        // wider than the 1200 default, so the terminal column got zero pixels.
        var toClose = WorkspaceViewport.FindPanelsToClose(1200, Rail, Minimum, AllPanels, null);

        Assert.Equal(new[] { WorkspacePanel.Agent, WorkspacePanel.Monitor }, toClose);
        Assert.True(TerminalWidthAfter(toClose) >= Minimum);
    }

    [Fact]
    public void WideWindow_KeepsEveryPanel()
    {
        var toClose = WorkspaceViewport.FindPanelsToClose(2560, Rail, Minimum, AllPanels, null);

        Assert.Empty(toClose);
    }

    [Fact]
    public void NarrowWindow_ClosesInPriorityOrderAndKeepsNavigationLast()
    {
        var toClose = WorkspaceViewport.FindPanelsToClose(900, Rail, Minimum, AllPanels, null);

        Assert.Equal(
            new[] { WorkspacePanel.Agent, WorkspacePanel.Monitor, WorkspacePanel.Sftp },
            toClose);
        Assert.DoesNotContain(WorkspacePanel.SessionSidebar, toClose);
    }

    [Fact]
    public void ExtremelyNarrowWindow_EventuallyClosesTheSessionTree()
    {
        var toClose = WorkspaceViewport.FindPanelsToClose(600, Rail, Minimum, AllPanels, null);

        Assert.Contains(WorkspacePanel.SessionSidebar, toClose);
    }

    [Fact]
    public void PanelTheUserJustOpened_IsNeverTheOneClosed()
    {
        var onlyAgent = new Dictionary<WorkspacePanel, double> { [WorkspacePanel.Agent] = AllPanels[WorkspacePanel.Agent] };

        var toClose = WorkspaceViewport.FindPanelsToClose(600, Rail, Minimum, onlyAgent, WorkspacePanel.Agent);

        // Refusing the click would look like a broken button, so the request wins and
        // the terminal takes the narrow layout instead.
        Assert.Empty(toClose);
    }

    [Fact]
    public void AlreadyNarrowLayout_WithNothingOpen_ChangesNothing()
    {
        var toClose = WorkspaceViewport.FindPanelsToClose(
            600, Rail, Minimum, new Dictionary<WorkspacePanel, double>(), null);

        Assert.Empty(toClose);
    }

    [Fact]
    public void SacrificeOrder_MatchesTheDocumentedRanking()
    {
        Assert.Equal(
            new[]
            {
                WorkspacePanel.Agent,
                WorkspacePanel.Monitor,
                WorkspacePanel.Sftp,
                WorkspacePanel.SessionSidebar
            },
            WorkspaceViewport.SacrificeOrder);
    }

    private static double TerminalWidthAfter(IReadOnlyList<WorkspacePanel> closed)
    {
        var used = Rail + AllPanels.Where(pair => !closed.Contains(pair.Key))
            .Sum(pair => pair.Value);
        return 1200 - used;
    }

    /// <summary>
    /// The guard adds up what the XAML charges each surface. These assertions fail if
    /// a width is changed on one side only, which would otherwise degrade panels at
    /// the wrong moment.
    /// </summary>
    public sealed class XamlWidthsMatchTheGuard
    {
        [Fact]
        public void NavRailSessionSidebarAndMonitorMatchTheConstants()
        {
            var styles = XDocument.Load(SourceFile("Themes", "Styles.axaml"));
            var rail = DescendantElements(styles, "Style")
                .Where(style => (string?)style.Attribute("Selector") == "Border.cx-nav-rail")
                .Descendants()
                .First(element => element.Name.LocalName == "Setter"
                                  && (string?)element.Attribute("Property") == "Width")
                .Attribute("Value")!.Value;

            Assert.Equal(MainWindowViewModel.NavRailWidth, double.Parse(rail));

            var window = XDocument.Load(SourceFile("Views", "MainWindow.axaml"));
            var sidebar = DescendantElements(window, "Border")
                .Where(border => (string?)border.Attribute("Grid.Column") == "1")
                .Select(border => border.Attribute("Width")?.Value)
                .Single(width => width != null);

            Assert.Equal(MainWindowViewModel.SessionSidebarWidth, double.Parse(sidebar));

            var monitor = DescendantElements(window, "ServerMonitorView")
                .Select(view => view.Attribute("Width")?.Value)
                .Single(width => width != null);

            Assert.Equal(
                MainWindowViewModel.MonitorPanelWidth - 3,
                double.Parse(monitor));
        }

        private static string SourceFile(params string[] parts)
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "CxShell.csproj")))
                directory = directory.Parent;

            return Path.Combine([directory!.FullName, .. parts]);
        }

        private static IEnumerable<XElement> DescendantElements(XDocument document, string localName)
            => document.DescendantNodes().OfType<XElement>()
                .Where(element => element.Name.LocalName == localName);
    }
}
