using SyncWallpaper.Update.Core;

namespace SyncWallpaper.App;

public sealed class UpdateCheckCompletedEventArgs(UpdateCheckResult result, bool manual) : EventArgs
{
    public UpdateCheckResult Result { get; } = result;
    public bool Manual { get; } = manual;
}
