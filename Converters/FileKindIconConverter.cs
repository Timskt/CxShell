using System;
using System.Globalization;
using Avalonia.Data.Converters;
using CxShell.Models;
using CxShell.Services;
using CxShell.ViewModels;

namespace CxShell.Converters;

/// Keeps Models free of Avalonia types while the file browser shows real glyphs
/// instead of emoji.
public sealed class FileKindIconConverter : IValueConverter
{
    public static FileKindIconConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            SftpFileItem item => FileKindIconCatalog.IconFor(item.Name, item.IsDirectory),
            SftpPathSuggestionItem suggestion => FileKindIconCatalog.IconFor(suggestion.Name, suggestion.IsDirectory),
            _ => null
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
