namespace Alacrity.Core;

/// <summary>Optional host-renderer transaction used to discard deferred commands from a failed widget.</summary>
public interface IPluginHudRenderTransaction
{
    void BeginWidget();
    void CommitWidget();
    void RollbackWidget();
}
