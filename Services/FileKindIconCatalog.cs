using System;
using System.IO;
using AtomUI.Icons.AntDesign;
using Avalonia.Controls;

namespace CxShell.Services;

public static class FileKindIconCatalog
{
    public static PathIcon IconFor(string fileName, bool isDirectory)
    {
        var kind = isDirectory
            ? AntDesignIconKind.FolderOpenOutlined
            : KindForExtension(Path.GetExtension(fileName).ToLowerInvariant());

        return (PathIcon)new AntDesignIconProvider(kind).ProvideValue(null!);
    }

    private static AntDesignIconKind KindForExtension(string extension) => extension switch
    {
        ".txt" or ".log" or ".md" or ".rst" => AntDesignIconKind.FileTextOutlined,
        ".sh" or ".bash" or ".zsh" or ".fish" or ".ps1" => AntDesignIconKind.CodeOutlined,
        ".json" or ".yaml" or ".yml" or ".toml" or ".xml" or ".ini" or ".conf" => AntDesignIconKind.SettingOutlined,
        ".py" or ".rb" or ".js" or ".ts" or ".go" or ".rs" or ".cs" or ".java" or ".c" or ".h" or ".cpp" => AntDesignIconKind.CodeOutlined,
        ".zip" or ".tar" or ".gz" or ".bz2" or ".xz" or ".7z" or ".rar" => AntDesignIconKind.FileZipOutlined,
        ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".svg" or ".webp" or ".ico" => AntDesignIconKind.FileImageOutlined,
        ".mp3" or ".wav" or ".flac" or ".ogg" or ".m4a" => AntDesignIconKind.SoundOutlined,
        ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" => AntDesignIconKind.VideoCameraOutlined,
        ".pdf" => AntDesignIconKind.FilePdfOutlined,
        ".xls" or ".xlsx" or ".csv" => AntDesignIconKind.FileExcelOutlined,
        ".doc" or ".docx" => AntDesignIconKind.FileWordOutlined,
        ".ppt" or ".pptx" => AntDesignIconKind.FilePptOutlined,
        ".exe" or ".msi" or ".dmg" or ".app" or ".deb" or ".rpm" => AntDesignIconKind.WindowsOutlined,
        ".pem" or ".key" or ".pub" or ".pfx" or ".crt" => AntDesignIconKind.SafetyCertificateOutlined,
        ".db" or ".sqlite" or ".sqlite3" or ".sql" => AntDesignIconKind.DatabaseOutlined,
        _ => AntDesignIconKind.FileOutlined
    };
}
