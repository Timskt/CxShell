using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CxShell.Models;

public class SftpFileItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public bool IsDirectory { get; set; }
    public long Size { get; set; }
    public DateTime LastModified { get; set; }
    public string Permissions { get; set; } = "";
    public bool IsSymLink { get; set; }
    public string? SymLinkTarget { get; set; }

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    private bool _isRenaming;
    public bool IsRenaming
    {
        get => _isRenaming;
        set
        {
            _isRenaming = value;
            if (value) _renamingText = Name; // 初始化为当前名称
            OnPropertyChanged();
        }
    }

    private string _renamingText = "";
    public string RenamingText
    {
        get => _renamingText;
        set { _renamingText = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public string SizeDisplay => IsDirectory ? "-" : FormatSize(Size);
    public string DateDisplay => LastModified.ToString("MM-dd HH:mm");

    public string DisplayName => IsSymLink && SymLinkTarget != null
        ? $"{Name} → {SymLinkTarget}"
        : Name;

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
    }
}
