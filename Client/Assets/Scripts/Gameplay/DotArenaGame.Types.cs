#nullable enable

using Shared.Interfaces;
using TMPro;
using UnityEngine;

namespace SampleClient.Gameplay
{
    internal sealed class PlayerRenderState
    {
        public Vector2 PreviousPosition { get; set; }
        public Vector2 TargetPosition { get; set; }
        public float ReceivedAt { get; set; }
        public PlayerLifeState State { get; set; }
        public bool Alive { get; set; }
        public int Score { get; set; }
        public int SpeedBoostRemainingSeconds { get; set; }
        public int KnockbackBoostRemainingSeconds { get; set; }
    }

    internal sealed class PlayerOverlayView
    {
        public PlayerOverlayView(GameObject root, RectTransform rootRect, TextMeshProUGUI nameText, TextMeshProUGUI scoreText)
        {
            Root = root;
            RootRect = rootRect;
            NameText = nameText;
            ScoreText = scoreText;
        }

        public GameObject Root { get; }
        public RectTransform RootRect { get; }
        public TextMeshProUGUI NameText { get; }
        public TextMeshProUGUI ScoreText { get; }
    }

    internal enum EntryMenuState
    {
        Hidden = 0,
        ModeSelect = 1,
        MultiplayerAuth = 2
    }

    internal enum SessionMode
    {
        None = 0,
        SinglePlayer = 1,
        Multiplayer = 2
    }
}
