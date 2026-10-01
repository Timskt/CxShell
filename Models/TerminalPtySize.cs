namespace CxShell.Models;

/// <summary>Terminal grid dimensions, with optional physical pixel dimensions for SSH PTYs.</summary>
public readonly record struct TerminalPtySize(
    int Columns,
    int Rows,
    int PixelWidth = 0,
    int PixelHeight = 0);
