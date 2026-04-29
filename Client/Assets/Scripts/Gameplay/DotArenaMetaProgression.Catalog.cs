#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SampleClient.Gameplay
{
    internal static partial class DotArenaMetaProgression
    {
        private static readonly DotArenaShopItem[] ShopCatalog =
        {
            new() { Id = "skin_default", Name = "Default Skin", Price = 0 },
            new() { Id = "skin_crimson", Name = "Crimson Skin", Price = 120 },
            new() { Id = "skin_glacier", Name = "Glacier Skin", Price = 180 },
            new() { Id = "skin_sunburst", Name = "Sunburst Skin", Price = 240, IsStarterOffer = true }
        };

        public static IReadOnlyList<DotArenaShopItem> GetShopCatalog() => ShopCatalog;
        private static DotArenaShopItem? FindItem(string itemId)
        {
            foreach (var item in ShopCatalog)
            {
                if (item.Id == itemId)
                {
                    return item;
                }
            }

            return null;
        }

    }
}
