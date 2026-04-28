#nullable enable

using System.Collections.Generic;
using TMPro;
using ULinkRPC.Client;
using UnityEngine;

namespace SampleClient.Gameplay
{
    internal struct DotArenaImmediateHudSnapshot
    {
        public string Status { get; set; }
        public string LocalPlayerId { get; set; }
        public string Account { get; set; }
        public string LocalPlayerScoreText { get; set; }
        public int LocalWinCount { get; set; }
        public int LastWorldTick { get; set; }
        public string LocalPlayerBuffText { get; set; }
        public SessionMode SessionMode { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }
        public string Path { get; set; }
        public string EventMessage { get; set; }
        public float PlayerVisualDiameter { get; set; }
    }

    internal static class DotArenaImmediateHudRenderer
    {
        public static void DrawSessionHud(
            in DotArenaImmediateHudSnapshot snapshot,
            IReadOnlyDictionary<string, DotView> views,
            IReadOnlyDictionary<string, PlayerRenderState> renderStates)
        {
            const float width = 400f;
            const float height = 160f;

            var boxRect = new Rect(16f, 16f, width, height);
            var contentRect = new Rect(28f, 24f, width - 24f, height - 16f);

            var previousColor = GUI.color;
            GUI.color = new Color(0.04f, 0.06f, 0.08f, 0.9f);
            GUI.Box(boxRect, GUIContent.none);
            GUI.color = previousColor;

            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };

            var bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.86f, 0.91f, 0.96f, 1f) }
            };

            GUI.Label(new Rect(contentRect.x, contentRect.y, contentRect.width, 24f), "ULinkRPC Dot Arena", titleStyle);
            GUI.Label(new Rect(contentRect.x, contentRect.y + 24f, contentRect.width, 18f), $"状态: {snapshot.Status}", bodyStyle);
            GUI.Label(new Rect(contentRect.x, contentRect.y + 44f, contentRect.width, 18f),
                $"玩家: {(snapshot.LocalPlayerId.Length > 0 ? snapshot.LocalPlayerId : snapshot.Account)}   分数/质量: {snapshot.LocalPlayerScoreText}   胜场: {snapshot.LocalWinCount}", bodyStyle);
            GUI.Label(new Rect(contentRect.x, contentRect.y + 64f, contentRect.width, 18f),
                $"服务端 Tick: {snapshot.LastWorldTick}   同步人数: {views.Count}   状态: {snapshot.LocalPlayerBuffText}", bodyStyle);
            GUI.Label(new Rect(contentRect.x, contentRect.y + 84f, contentRect.width, 18f),
                snapshot.SessionMode == SessionMode.SinglePlayer
                    ? "模式: 本地单机"
                    : $"地址: {Rpc.WebSocketRpcClientFactory.BuildUrl(snapshot.Host, snapshot.Port, snapshot.Path)}", bodyStyle);

            GUI.Label(new Rect(contentRect.x, contentRect.y + 104f, contentRect.width, 18f),
                "W/A/S/D 移动。吃豆成长，躲开更大的球，主动吞掉更小的球。", bodyStyle);
            GUI.Label(new Rect(contentRect.x, contentRect.y + 124f, contentRect.width, 18f),
                $"事件: {snapshot.EventMessage}", bodyStyle);

            DrawPlayerOverlays(views, renderStates, snapshot.PlayerVisualDiameter);
        }

        private static void DrawPlayerOverlays(
            IReadOnlyDictionary<string, DotView> views,
            IReadOnlyDictionary<string, PlayerRenderState> renderStates,
            float playerVisualDiameter)
        {
            var camera = Camera.main;
            if (camera == null || views.Count == 0)
            {
                return;
            }

            var pixelsPerWorldUnit = Screen.height / (camera.orthographicSize * 2f);
            var diameterPixels = playerVisualDiameter * pixelsPerWorldUnit;
            var labelWidth = Mathf.Max(96f, diameterPixels * 2f);
            var nameHeight = Mathf.Max(18f, diameterPixels * 0.36f);
            var scoreHeight = Mathf.Max(16f, diameterPixels * 0.3f);

            var nameStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = Mathf.RoundToInt(Mathf.Clamp(diameterPixels * 0.24f, 14f, 22f)),
                clipping = TextClipping.Overflow,
                normal = { textColor = new Color(0.94f, 0.97f, 1f, 1f) }
            };

            var scoreStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = Mathf.RoundToInt(Mathf.Clamp(diameterPixels * 0.22f, 13f, 20f)),
                clipping = TextClipping.Overflow,
                normal = { textColor = new Color(1f, 0.97f, 0.78f, 1f) }
            };

            foreach (var entry in views)
            {
                if (!renderStates.TryGetValue(entry.Key, out var renderState))
                {
                    continue;
                }

                var worldPosition = entry.Value.Root.transform.position;
                var screenPosition = camera.WorldToScreenPoint(worldPosition);
                if (screenPosition.z <= 0f)
                {
                    continue;
                }

                var centerX = screenPosition.x;
                var centerY = Screen.height - screenPosition.y;
                var nameRect = new Rect(centerX - (labelWidth * 0.5f), centerY - (nameHeight * 1.05f), labelWidth, nameHeight);
                var scoreRect = new Rect(centerX - (labelWidth * 0.5f), centerY + (scoreHeight * 0.05f), labelWidth, scoreHeight);

                GUI.Label(nameRect, entry.Key, nameStyle);
                GUI.Label(scoreRect, $"mass: {DotArenaPresentation.FormatMass(renderState.Mass)}", scoreStyle);
            }
        }
    }
}
