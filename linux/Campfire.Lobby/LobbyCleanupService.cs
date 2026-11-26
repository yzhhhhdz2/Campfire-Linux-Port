// Campfire Linux port — 新增文件，依据 Xocio/Campfire v1.1.32 (GPL-3.0) 的 LobbyPublisher.cs 心跳契约新建。
// 修改：服务端按心跳缺席判过期，过期后对该 id 返回 404，客户端会自动重新发布。

namespace Campfire.Lobby;

internal sealed class LobbyCleanupService : BackgroundService
{
    private readonly LobbyStore _store;
    private readonly LobbyOptions _options;

    public LobbyCleanupService(LobbyStore store, LobbyOptions options)
    {
        _store = store;
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                _store.PurgeExpired(_options);
        }
        catch (OperationCanceledException)
        {
            // 正常关机。
        }
    }
}
