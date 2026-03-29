namespace Server.Services;

internal static class GameArenaRuntimeRegistry
{
    private static GameArenaRuntime? _instance;

    public static GameArenaRuntime Instance =>
        _instance ?? throw new InvalidOperationException("Game arena runtime has not been initialized.");

    public static void Initialize(GameArenaRuntime instance)
    {
        _instance = instance;
    }
}
