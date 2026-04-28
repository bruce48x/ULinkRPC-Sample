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

        public static string FormatMass(float mass)
        {
            return mass >= 100f ? mass.ToString("0") : mass.ToString("0.0");
        }

        public static Color ResolvePlayerColor(string playerId, string? cosmeticId = null)
        {
            if (!string.IsNullOrWhiteSpace(cosmeticId))
            {
                return ResolveCosmeticColor(cosmeticId);
            }

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
                PickupType.Shield => ShieldPickupColor,
                PickupType.BonusScore => BonusScorePickupColor,
                _ => Color.white
            };
        }

        public static string GetPickupDisplayName(PickupType pickupType)
        {
            return pickupType switch
            {
                PickupType.ScorePoint => "Mass",
                PickupType.SpeedBoost => "Mass",
                PickupType.KnockbackBoost => "Mass",
                PickupType.Shield => "Mass",
                PickupType.BonusScore => "Mass",
                _ => "Food"
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

        private static Color ResolveCosmeticColor(string cosmeticId)
        {
            return cosmeticId switch
            {
                "skin_crimson" => new Color(0.92f, 0.22f, 0.28f, 1f),
                "skin_glacier" => new Color(0.42f, 0.78f, 1f, 1f),
                "skin_sunburst" => new Color(1f, 0.74f, 0.18f, 1f),
                _ => new Color(0.3f, 0.78f, 0.96f, 1f)
            };
        }
    }
}
