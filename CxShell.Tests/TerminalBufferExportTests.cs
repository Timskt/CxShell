using CxShell.Terminal;

namespace CxShell.Tests;

public sealed class TerminalBufferExportTests
{
    [Fact]
    public void ExportText_CombinesScrollbackAndCurrentScreenWithoutTrailingWhitespace()
    {
        var buffer = new TerminalBuffer(columns: 12, rows: 2, maxScrollback: 20);
        PutText(buffer, "first line");
        buffer.ClearScreen();
        buffer.MoveCursor(0, 0);
        PutText(buffer, "中文 second");

        Assert.Equal("first line\n中文 second", buffer.ExportText());
    }

    [Fact]
    public void ExportText_RemovesTrailingBlankRows()
    {
        var buffer = new TerminalBuffer(columns: 8, rows: 3);
        PutText(buffer, "line");

        Assert.Equal("line", buffer.ExportText());
    }

    [Fact]
    public void ExportText_JoinsAutomaticallyWrappedRows()
    {
        var buffer = new TerminalBuffer(columns: 5, rows: 2, maxScrollback: 20);
        new AnsiParser(buffer).Process("abcdefghijkl");

        Assert.Equal("abcdefghijkl", buffer.ExportText());
    }

    [Fact]
    public void ExportText_PreservesSpacesInsideAutomaticallyWrappedLines()
    {
        var buffer = new TerminalBuffer(columns: 5, rows: 2, maxScrollback: 20);
        new AnsiParser(buffer).Process("ab   X");

        Assert.Equal("ab   X", buffer.ExportText());
    }

    [Fact]
    public void ExportText_PreservesWrittenSpaceBeforeWideCharacterWrap()
    {
        var buffer = new TerminalBuffer(columns: 5, rows: 2, maxScrollback: 20);
        new AnsiParser(buffer).Process("abcd 中");

        Assert.Equal("abcd 中", buffer.ExportText());
    }

    [Fact]
    public void ExportText_OmitsWideCharacterWrapGap()
    {
        var buffer = new TerminalBuffer(columns: 5, rows: 2, maxScrollback: 20);
        new AnsiParser(buffer).Process("abcd中X");

        Assert.Equal("abcd中X", buffer.ExportText());
    }

    [Fact]
    public void ExportText_KeepsExplicitLineBreakAfterFullWidthRow()
    {
        var buffer = new TerminalBuffer(columns: 5, rows: 2, maxScrollback: 20);
        new AnsiParser(buffer).Process("abcde\nx");

        Assert.Equal("abcde\nx", buffer.ExportText());
    }

    private static void PutText(TerminalBuffer buffer, string text)
    {
        foreach (var character in text)
            buffer.PutChar(character);
    }
}
