namespace Fontager.Viewer.ViewModels;

/// <summary>
/// A persisted recent font path shown on the empty state.
/// </summary>
public sealed class RecentFileItem
{
    public RecentFileItem(string filePath)
    {
        Path = filePath;
        FileName = System.IO.Path.GetFileName(filePath);
        var dir = System.IO.Path.GetDirectoryName(filePath);
        DirectoryLabel = string.IsNullOrEmpty(dir) ? string.Empty : dir;
    }

    public string Path { get; }

    public string FileName { get; }

    public string DirectoryLabel { get; }
}
