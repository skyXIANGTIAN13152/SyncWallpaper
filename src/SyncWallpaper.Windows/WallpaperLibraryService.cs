using System;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using SyncWallpaper.Core;

namespace SyncWallpaper.Windows;

public sealed class WallpaperLibraryService
{
    private readonly ConfigurationStore _store;
    public WallpaperLibraryService(ConfigurationStore store) => _store = store;
    public LibraryDocument Load() => _store.Load("library.json", new LibraryDocument());
    public WallpaperAsset? Find(string id) => Load().Assets.FirstOrDefault(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Reconciles the persisted library with the files currently on disk.
    /// Missing files are marked instead of being removed so profile bindings
    /// remain explainable and can recover if a file is restored later.
    /// </summary>
    public WallpaperLibraryRefreshResult Refresh()
    {
        var document = Load();
        var missing = 0;
        var recovered = 0;
        var changed = false;
        foreach (var asset in document.Assets)
        {
            var exists = File.Exists(ResolvePath(asset));
            if (asset.IsMissing == !exists) continue;
            asset.IsMissing = !exists;
            changed = true;
            if (exists) recovered++; else missing++;
        }

        if (changed) _store.Save("library.json", document);
        return new WallpaperLibraryRefreshResult(document, missing, recovered);
    }

    public WallpaperAsset Import(string sourcePath, string? displayName = null)
    {
        var doc = Load(); var hash = FileUtilities.Sha256(sourcePath);
        var existing = doc.Assets.FirstOrDefault(a => a.Sha256.Equals(hash, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;
        var extension = System.IO.Path.GetExtension(sourcePath).ToLowerInvariant();
        var id = Guid.NewGuid().ToString("N"); var managed = Path.Combine("Wallpapers", "wp_" + id + extension);
        var destination = Path.Combine(_store.Paths.Root, managed); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(sourcePath, destination, false);
        var format = extension.TrimStart('.');
        var asset = new WallpaperAsset { Id = id, DisplayName = displayName ?? Path.GetFileNameWithoutExtension(sourcePath), OriginalFileName = Path.GetFileName(sourcePath), ManagedRelativePath = managed, Sha256 = hash, FileSize = new FileInfo(destination).Length, Format = format, ImportedAt = DateTime.UtcNow };
        try
        {
            var frame = BitmapFrame.Create(new Uri(destination), BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnLoad);
            asset.Width = frame.PixelWidth; asset.Height = frame.PixelHeight;
        }
        catch { asset.IsMissing = true; }
        doc.Assets.Add(asset); _store.Save("library.json", doc); return asset;
    }

    public bool SoftDelete(string assetId, ProfilesDocument profiles)
    {
        var doc = Load(); var asset = doc.Assets.FirstOrDefault(a => a.Id.Equals(assetId, StringComparison.OrdinalIgnoreCase));
        if (asset is null) return false;
        if (profiles.Profiles.SelectMany(p => p.Roles).Any(r => r.WallpaperAssetId.Equals(assetId, StringComparison.OrdinalIgnoreCase)))
            return false;
        var path = Path.Combine(_store.Paths.Root, asset.ManagedRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(path))
        {
            Directory.CreateDirectory(_store.Paths.Deleted);
            File.Move(path, Path.Combine(_store.Paths.Deleted, Path.GetFileName(path) + "." + DateTime.UtcNow.ToString("yyyyMMddHHmmss")), true);
        }
        doc.Assets.Remove(asset); _store.Save("library.json", doc); return true;
    }

    private string ResolvePath(WallpaperAsset asset)
        => asset.StorageMode.Equals("External", StringComparison.OrdinalIgnoreCase)
            ? asset.ExternalPath ?? string.Empty
            : Path.Combine(_store.Paths.Root, asset.ManagedRelativePath.Replace('/', Path.DirectorySeparatorChar));
}

public sealed record WallpaperLibraryRefreshResult(LibraryDocument Document, int MissingCount, int RecoveredCount);
