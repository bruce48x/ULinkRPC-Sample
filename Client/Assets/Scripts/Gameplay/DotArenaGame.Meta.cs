#nullable enable

namespace SampleClient.Gameplay
{
    public sealed partial class DotArenaGame
    {
        private void EnsureMetaState(string playerId)
        {
            _metaState = DotArenaMetaProgression.LoadOrCreate(playerId);
        }
    }
}
