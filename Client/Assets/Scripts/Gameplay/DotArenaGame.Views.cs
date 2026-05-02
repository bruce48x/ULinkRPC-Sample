#nullable enable

using System;
using System.Threading.Tasks;
using Shared.Interfaces;
using UnityEngine;
using static SampleClient.Gameplay.DotArenaTuning;

namespace SampleClient.Gameplay
{
    public sealed partial class DotArenaGame
    {
        private void ResetSessionPresentation()
        {
            Debug.Log($"[DotArena] ResetSessionPresentation views={_views.Count}, pickups={_pickupViews.Count}, mode={_sessionMode}");
            foreach (var view in _views.Values)
            {
                Destroy(view.Root);
            }

            foreach (var pickupView in _pickupViews)
            {
                Destroy(pickupView.Root);
            }

            _overlayPresenter.Clear(Destroy);
            _views.Clear();
            _pickupViews.Clear();
            _renderStates.Clear();
            _callbackInbox.Clear();
            _localWinCount = _sessionMode == SessionMode.Multiplayer ? _localWinCount : 0;
            _lastWorldTick = -1;
            _lastLoggedPlayerCount = -1;
            _dashQueued = false;
            _nextInputAt = 0f;
            _singlePlayerTickAccumulator = 0f;
            _currentArenaHalfExtents = GameplayConfig.ArenaHalfExtents;
            UpdateArenaVisuals();
        }

        private void UpdateViews()
        {
            foreach (var entry in _views)
            {
                if (!_renderStates.TryGetValue(entry.Key, out var renderState))
                {
                    continue;
                }

                var elapsed = Mathf.Clamp01((Time.time - renderState.ReceivedAt) / InterpolationDurationSeconds);
                var smoothed = elapsed * elapsed * (3f - (2f * elapsed));
                var position = Vector2.Lerp(renderState.PreviousPosition, renderState.TargetPosition, smoothed);
                entry.Value.SetPosition(position);
            }

            UpdateCameraFollow();
            _overlayPresenter.UpdateOverlayViews(_sceneUiPresenter, _views, _renderStates);

            var pickupScale = GameplayConfig.PickupCollisionRadius * 2f;
            foreach (var pickupView in _pickupViews)
            {
                var pulse = 1f + (Mathf.Sin(Time.time * PickupPulseFrequency) * PickupPulseAmplitude);
                pickupView.UpdateVisual(Time.time, pickupScale * pulse, PickupAbsorbDurationSeconds);
            }

            foreach (var dotView in _views.Values)
            {
                dotView.UpdateJelly(Time.time);
            }
        }

        private void UpdateCameraFollow()
        {
            var camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            var targetPosition = Vector2.zero;
            var desiredCameraSize = FollowCameraSize;
            if (_views.TryGetValue(_localPlayerId, out var localView))
            {
                targetPosition = localView.GetPosition();
            }

            if (_renderStates.TryGetValue(_localPlayerId, out var localState))
            {
                desiredCameraSize = Mathf.Clamp(
                    Mathf.Max(FollowCameraSize, localState.Radius * FollowCameraRadiusMultiplier),
                    FollowCameraSize,
                    MaxFollowCameraSize);
            }

            var zoomT = 1f - Mathf.Exp(-CameraZoomSharpness * Time.deltaTime);
            camera.orthographicSize = Mathf.Lerp(camera.orthographicSize, desiredCameraSize, zoomT);

            var halfVisibleHeight = Mathf.Max(0f, camera.orthographicSize - ArenaVisualPadding);
            var halfVisibleWidth = halfVisibleHeight * camera.aspect;
            var limitX = Mathf.Max(0f, CurrentArenaHalfWidth - halfVisibleWidth);
            var limitY = Mathf.Max(0f, CurrentArenaHalfHeight - halfVisibleHeight);
            var desired = new Vector3(
                Mathf.Clamp(targetPosition.x, -limitX, limitX),
                Mathf.Clamp(targetPosition.y, -limitY, limitY),
                -10f);
            var t = 1f - Mathf.Exp(-CameraFollowSharpness * Time.deltaTime);
            camera.transform.position = Vector3.Lerp(camera.transform.position, desired, t);
        }

        private DotView CreateView(string playerId)
        {
            var viewRoot = new GameObject(playerId);
            viewRoot.transform.SetParent(transform, false);
            Debug.Log($"[DotArena] CreateView root={viewRoot.name} parent={viewRoot.transform.parent?.name}");

            var renderer = viewRoot.AddComponent<SpriteRenderer>();
            renderer.sprite = _playerSprite;
            var cosmeticId = playerId == _localPlayerId ? _metaState?.EquippedCosmeticId : null;
            renderer.color = DotArenaPresentation.ResolvePlayerColor(playerId, cosmeticId);
            renderer.sortingOrder = PlayerSortingOrder;
            renderer.material = CreateJellyMaterial(UnityEngine.Random.Range(0f, Mathf.PI * 2f), 3.6f, 0.06f);

            var outlineObject = new GameObject("Outline");
            outlineObject.transform.SetParent(viewRoot.transform, false);
            outlineObject.transform.localPosition = new Vector3(0f, 0f, -0.01f);
            var outlineRenderer = outlineObject.AddComponent<SpriteRenderer>();
            outlineRenderer.sprite = _playerOutlineSprite;
            outlineRenderer.color = PlayerOutlineColor;
            outlineRenderer.sortingOrder = PlayerSortingOrder - 1;
            outlineRenderer.material = CreateJellyMaterial(UnityEngine.Random.Range(0f, Mathf.PI * 2f), 4.2f, 0.08f);

            var nameBackdrop = new GameObject("NameBackdrop");
            nameBackdrop.transform.SetParent(viewRoot.transform, false);
            nameBackdrop.transform.localPosition = new Vector3(0f, PlayerNameOffsetY, PlayerTextDepth + 0.01f);
            nameBackdrop.transform.localScale = new Vector3(PlayerNameBackdropWidth, PlayerNameBackdropHeight, 1f);
            var nameBackdropRenderer = nameBackdrop.AddComponent<SpriteRenderer>();
            nameBackdropRenderer.sprite = _pixelSprite;
            nameBackdropRenderer.color = PlayerTextBackdropColor;
            nameBackdropRenderer.sortingOrder = PlayerTextBackdropSortingOrder;

            var nameLabel = new GameObject("NameLabel");
            nameLabel.transform.SetParent(viewRoot.transform, false);
            nameLabel.transform.localPosition = new Vector3(0f, PlayerNameOffsetY, PlayerTextDepth);
            nameLabel.transform.localScale = Vector3.one * PlayerNameScale;

            var nameText = nameLabel.AddComponent<TextMesh>();
            nameText.text = playerId;
            nameText.fontSize = 48;
            nameText.characterSize = PlayerTextCharacterSize;
            nameText.anchor = TextAnchor.MiddleCenter;
            nameText.alignment = TextAlignment.Center;
            nameText.fontStyle = FontStyle.Bold;
            nameText.color = new Color(0.98f, 0.99f, 1f, 1f);
            DotArenaSpriteFactory.ConfigureTextRenderer(nameText.GetComponent<MeshRenderer>(), PlayerTextSortingOrder);

            var scoreBackdrop = new GameObject("ScoreBackdrop");
            scoreBackdrop.transform.SetParent(viewRoot.transform, false);
            scoreBackdrop.transform.localPosition = new Vector3(0f, PlayerScoreOffsetY, PlayerTextDepth + 0.01f);
            scoreBackdrop.transform.localScale = new Vector3(PlayerScoreBackdropWidth, PlayerScoreBackdropHeight, 1f);
            var scoreBackdropRenderer = scoreBackdrop.AddComponent<SpriteRenderer>();
            scoreBackdropRenderer.sprite = _pixelSprite;
            scoreBackdropRenderer.color = PlayerTextBackdropColor;
            scoreBackdropRenderer.sortingOrder = PlayerTextBackdropSortingOrder;

            var scoreLabel = new GameObject("ScoreLabel");
            scoreLabel.transform.SetParent(viewRoot.transform, false);
            scoreLabel.transform.localPosition = new Vector3(0f, PlayerScoreOffsetY, PlayerTextDepth);
            scoreLabel.transform.localScale = Vector3.one * PlayerScoreScale;

            var scoreText = scoreLabel.AddComponent<TextMesh>();
            scoreText.text = "0";
            scoreText.fontSize = 44;
            scoreText.characterSize = PlayerTextCharacterSize;
            scoreText.anchor = TextAnchor.MiddleCenter;
            scoreText.alignment = TextAlignment.Center;
            scoreText.fontStyle = FontStyle.Bold;
            scoreText.color = new Color(1f, 0.97f, 0.72f, 1f);
            DotArenaSpriteFactory.ConfigureTextRenderer(scoreText.GetComponent<MeshRenderer>(), PlayerTextSortingOrder);

            var view = new DotView(viewRoot, renderer, outlineRenderer, nameText, scoreText);
            view.SetIdentity(playerId, 0);
            view.ApplyPresentation(DotArenaPresentation.ResolvePlayerColor(playerId, cosmeticId), PlayerLifeState.Idle, true, GameplayConfig.PlayerVisualRadius);
            return view;
        }

        private PickupView CreatePickupView(PickupType pickupType)
        {
            var pickupRoot = new GameObject($"{pickupType}Pickup");
            pickupRoot.transform.SetParent(transform, false);

            var pickupColor = DotArenaPresentation.GetPickupColor(pickupType);
            var renderer = pickupRoot.AddComponent<SpriteRenderer>();
            renderer.sprite = _playerSprite;
            renderer.color = pickupColor;
            renderer.sortingOrder = PickupSortingOrder;
            renderer.material = CreatePickupAbsorbMaterial(pickupColor);

            var glow = new GameObject("Glow");
            glow.transform.SetParent(pickupRoot.transform, false);
            glow.transform.localPosition = new Vector3(0f, 0f, 0.01f);
            glow.transform.localScale = Vector3.one * 1.24f;

            var glowRenderer = glow.AddComponent<SpriteRenderer>();
            glowRenderer.sprite = _playerOutlineSprite;
            glowRenderer.color = Color.Lerp(pickupColor, Color.white, 0.35f);
            glowRenderer.sortingOrder = PickupSortingOrder - 1;

            var label = new GameObject("Label");
            label.transform.SetParent(pickupRoot.transform, false);
            label.transform.localPosition = new Vector3(0f, 0f, PlayerTextDepth);
            label.transform.localScale = Vector3.one * PickupLabelScale;

            var labelText = label.AddComponent<TextMesh>();
            labelText.text = DotArenaPresentation.GetPickupDisplayName(pickupType);
            labelText.fontSize = 64;
            labelText.characterSize = 0.12f;
            labelText.anchor = TextAnchor.MiddleCenter;
            labelText.alignment = TextAlignment.Center;
            labelText.fontStyle = FontStyle.Bold;
            labelText.color = DotArenaPresentation.GetPickupLabelColor(pickupType);
            DotArenaSpriteFactory.ConfigureTextRenderer(labelText.GetComponent<MeshRenderer>(), PickupLabelSortingOrder);

            pickupRoot.SetActive(false);
            return new PickupView(pickupRoot, renderer, glowRenderer, labelText);
        }

        private Material CreatePickupAbsorbMaterial(Color baseColor)
        {
            if (_pickupAbsorbShader == null)
            {
                _pickupAbsorbShader = Shader.Find(PickupAbsorbShaderName);
            }

            var shader = _pickupAbsorbShader != null ? _pickupAbsorbShader : Shader.Find("Sprites/Default");
            var material = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            if (_pickupAbsorbShader != null)
            {
                material.SetFloat("_Dissolve", 0f);
                if (material.HasProperty("_EdgeColor"))
                {
                    material.SetColor("_EdgeColor", Color.Lerp(baseColor, Color.white, 0.55f));
                }
            }

            return material;
        }

        private Material CreateJellyMaterial(float phase, float wobbleSpeed, float wobbleAmount)
        {
            if (_jellyShader == null)
            {
                _jellyShader = Shader.Find(JellyShaderName);
            }

            var shader = _jellyShader != null ? _jellyShader : Shader.Find("Sprites/Default");
            var material = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            if (_jellyShader != null)
            {
                material.SetFloat("_Phase", phase);
                material.SetFloat("_WobbleSpeed", wobbleSpeed);
                material.SetFloat("_WobbleAmount", wobbleAmount);
            }

            return material;
        }

    }
}
