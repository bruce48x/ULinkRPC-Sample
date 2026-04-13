#nullable enable

using Shared.Interfaces;
using UnityEngine;
using static SampleClient.Gameplay.DotArenaTuning;

namespace SampleClient.Gameplay
{
    internal static class DotArenaPresentation
    {
        public static string FormatScore(int score)
        {
            return score.ToString();
        }

        public static Color ResolvePlayerColor(string playerId)
        {
            var index = GetStableColorIndex(playerId);
            return RemotePalette[index];
        }

        public static Color GetPickupColor(PickupType pickupType)
        {
            return pickupType switch
            {
                PickupType.ScorePoint => ScorePickupColor,
                PickupType.SpeedBoost => SpeedPickupColor,
                PickupType.KnockbackBoost => KnockbackPickupColor,
                _ => Color.white
            };
        }

        public static string GetPickupDisplayName(PickupType pickupType)
        {
            return pickupType switch
            {
                PickupType.ScorePoint => "积分点",
                PickupType.SpeedBoost => "加速",
                PickupType.KnockbackBoost => "冲击力",
                _ => "Buff"
            };
        }

        public static Color GetPickupLabelColor(PickupType pickupType)
        {
            var color = GetPickupColor(pickupType);
            var luminance = (color.r * 0.299f) + (color.g * 0.587f) + (color.b * 0.114f);
            return luminance >= 0.6f ? Color.black : Color.white;
        }

        private static int GetStableColorIndex(string playerId)
        {
            unchecked
            {
                var hash = 2166136261u;
                foreach (var ch in playerId)
                {
                    hash ^= ch;
                    hash *= 16777619u;
                }

                return (int)(hash % (uint)RemotePalette.Length);
            }
        }
    }
}
