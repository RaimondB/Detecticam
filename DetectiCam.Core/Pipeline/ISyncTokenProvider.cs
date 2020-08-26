namespace DetectiCam.Core.Pipeline
{
    public interface ISyncTokenProvider
    {
        int? SyncToken { get; }
    }
}
