using System;
using System.Collections;
using System.IO;
using System.Linq;
using HandyDeviceControl.UI;
using HandyVideoPlayer.Playback;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace HandyVideoPlayer.Tests.Editor {
    public sealed class PlayerCanvasTests {
        private const string PlayerPrefab = "Assets/Prefabs/UI/HandyPlayer.prefab";
        private const string Shared = "Packages/com.handydevicecontrol.unity/Runtime/Prefabs/UI/";

        [Test] public void PlayerSceneUsesEditablePrefabsAndSharedConnectionControls() {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/VideoPlayer/HandyPlayer.unity");
            try {
                var roots = scene.GetRootGameObjects();
                var view = roots.SelectMany(g => g.GetComponentsInChildren<HandyPlayerView>()).Single();
                Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(view), Is.EqualTo(PlayerPrefab));
                Assert.That(roots.SelectMany(g => g.GetComponentsInChildren<EventSystem>()).Count(), Is.EqualTo(1));
                var form = view.GetComponentInChildren<HandyConnectionForm>(true);
                var status = view.GetComponentInChildren<HandyConnectionStatus>(true);
                Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(form), Is.EqualTo(Shared + "HandyConnectionForm.prefab"));
                Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(status), Is.EqualTo(Shared + "HandyConnectionStatus.prefab"));
                Assert.That(AssetDatabase.GetAssetPath(form.deviceOption), Is.EqualTo(Shared + "Parts/HandyDeviceOption.prefab"));
                Assert.That(AssetDatabase.GetAssetPath(view.tile), Is.EqualTo("Assets/Prefabs/UI/Parts/VideoTile.prefab"));
                using var data = new SerializedObject(view.GetComponent<HandyConnectionCanvas>());
                Assert.That(AssetDatabase.GetAssetPath(data.FindProperty("settings").objectReferenceValue), Is.EqualTo("Assets/Data/HandyConnection.asset"));
                foreach (var node in view.GetComponentsInChildren<Transform>(true))
                    Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(node.gameObject), Is.Zero, node.name);
            } finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        [TestCase(1280, 720, false)]
        [TestCase(720, 960, true)]
        [TestCase(640, 480, false)]
        public void WindowWidthChangesThePositionOfTheViewer(int width, int height, bool stacked) {
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab));
            try {
                go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                var rect = (RectTransform)go.transform;
                rect.sizeDelta = new Vector2(width, height);
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
                var panes = go.GetComponentInChildren<PlayerPaneLayout>();
                Assert.That(panes.viewer.rect.width, Is.GreaterThan(100));
                var view = go.GetComponent<HandyPlayerView>();
                Assert.That(view.stage.rect.height, Is.GreaterThan(120f));
                Assert.That(view.list.viewport.rect.height, Is.GreaterThan(40f));
                var bar = (RectTransform)view.GetComponentInChildren<HandyConnectionStatus>(true).transform;
                Assert.That(bar.rect.height, Is.InRange(55f, 100f));
                var s = RectTransformUtility.CalculateRelativeRectTransformBounds(panes.transform, panes.sidebar);
                var v = RectTransformUtility.CalculateRelativeRectTransformBounds(panes.transform, panes.viewer);
                if (stacked) Assert.That(v.center.y, Is.LessThan(s.center.y));
                else Assert.That(v.center.x, Is.GreaterThan(s.center.x));
            } finally { Object.DestroyImmediate(go); }
        }

        [UnityTearDown] public IEnumerator LeavePlayMode() {
            if (EditorApplication.isPlaying) yield return new ExitPlayMode();
        }

        [UnityTest] public IEnumerator TabsGallerySearchAndSeekUseThePrefabControls() {
            yield return new EnterPlayMode();
            var path = Path.Combine(Application.temporaryCachePath, "HandyCanvas_" + Guid.NewGuid().ToString("N"));
            var owner = new GameObject("영상 플레이어 테스트");
            owner.SetActive(false);
            try {
                Directory.CreateDirectory(path);
                var sample = Path.Combine(Application.dataPath, "Scripts/Tests/Editor/Fixtures/HevcSample.bytes");
                for (var i = 0; i < 45; i++) File.Copy(sample, Path.Combine(path, $"clip-{i:00}.mkv"));
                File.WriteAllText(Path.Combine(path, "clip-00.funscript"), "{\"actions\":[{\"at\":400,\"pos\":30},{\"at\":800,\"pos\":70}]}");
                var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab), owner.transform);
                var view = player.GetComponent<HandyPlayerView>();
                using (var data = new SerializedObject(player.GetComponent<HandyPlayerController>())) {
                    data.FindProperty("mediaFolder").stringValue = path;
                    data.ApplyModifiedPropertiesWithoutUndo();
                }
                owner.SetActive(true);
                yield return Until(() => view.count.text.Contains("표시 45") && view.list.content.childCount > 0);
                var tiles = view.list.GetComponentsInChildren<VideoTile>();
                Assert.That(tiles.Length, Is.LessThan(45));
                var tile = tiles.Single(t => t.caption.text == "clip-00");
                tile.button.onClick.Invoke();
                yield return Until(() => view.picture.texture && view.seek.maxValue > 2);
                Assert.That(view.picture.uvRect.height, Is.Negative);
                Assert.That(view.play.interactable, Is.False);
                view.first.onClick.Invoke();
                yield return Until(() => view.seek.interactable && view.seek.value > 0.25f);
                var events = EventSystem.current;
                if (!events) events = owner.AddComponent<EventSystem>();
                var pointer = new PointerEventData(events) {
                    position = RectTransformUtility.WorldToScreenPoint(null, view.settingsTab.transform.TransformPoint(
                        ((RectTransform)view.settingsTab.transform).rect.center)),
                    button = PointerEventData.InputButton.Left
                };
                var hits = new System.Collections.Generic.List<RaycastResult>();
                view.GetComponent<GraphicRaycaster>().Raycast(pointer, hits);
                Assert.That(hits, Is.Not.Empty);
                Assert.That(ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerClickHandler), Is.EqualTo(view.settingsTab.gameObject));
                Assert.That(view.settings.activeInHierarchy, Is.True);
                var connection = player.GetComponent<HandyConnectionCanvas>();
                var form = player.GetComponentInChildren<HandyConnectionForm>(true);
                var status = player.GetComponentInChildren<HandyConnectionStatus>(true);
                form.connectionKey.text = "test";
                Assert.That(form.check.interactable, Is.True);
                view.expand.onClick.Invoke();
                Assert.That(view.sidebar.activeInHierarchy, Is.False);
                Assert.That(status.gameObject.activeInHierarchy && connection.isActiveAndEnabled, Is.True);
                view.expand.onClick.Invoke();
                view.libraryTab.onClick.Invoke();
                Assert.That(form.connectionKey.text, Is.EqualTo("test"));
                yield return null;
                Canvas.ForceUpdateCanvases();
                view.list.verticalNormalizedPosition = 0;
                yield return null;
                yield return Until(() => view.list.GetComponentsInChildren<VideoTile>().Any(t => t.caption.text == "clip-44"));
                view.settingsTab.onClick.Invoke();
                view.filter.text = "clip-44";
                view.libraryTab.onClick.Invoke();
                yield return Until(() => view.count.text.Contains("표시 1") && !view.picture.texture
                    && view.list.GetComponentsInChildren<VideoTile>().Length == 1);
                var remaining = view.list.GetComponentsInChildren<VideoTile>().Single();
                Assert.That(remaining.caption.text, Is.EqualTo("clip-44"));
                Assert.That(remaining.missing.activeInHierarchy, Is.True);
                var tileRect = (RectTransform)remaining.transform;
                var originalWidth = tileRect.rect.width;
                view.list.viewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, originalWidth / 2);
                yield return Until(() => tileRect.rect.width > 0 && tileRect.rect.width < originalWidth * 0.75f);
            } finally {
                owner.SetActive(false);
                Object.Destroy(owner);
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
        }

        [UnityTest] public IEnumerator RecycledTileCanShowAPreviewAfterAFileDisappears() {
            yield return new EnterPlayMode();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/Parts/VideoTile.prefab");
            var instance = Object.Instantiate(prefab);
            using var loader = new VideoThumbnailLoader();
            try {
                var tile = instance.GetComponent<VideoTile>();
                var missing = new VideoLibrary.Video(Path.Combine(Application.temporaryCachePath, Guid.NewGuid() + ".mkv"), "missing", null);
                var sample = new VideoLibrary.Video(Path.Combine(Application.dataPath, "Scripts/Tests/Editor/Fixtures/HevcSample.bytes"), "sample", null);
                tile.Initialize(_ => { });
                tile.Bind(missing, null, loader);
                yield return Until(() => tile.unavailable.activeSelf);
                Assert.That(tile.picture.texture, Is.Null);
                Assert.That(tile.picture.enabled, Is.False);

                tile.Bind(sample, null, loader);
                tile.Bind(missing, null, loader);
                tile.Bind(sample, sample, loader);
                yield return Until(() => tile.picture.texture && tile.picture.enabled);
                Assert.That(tile.unavailable.activeSelf, Is.False);
                Assert.That(tile.button.image.color, Is.EqualTo(tile.selectedColor));
            } finally {
                Object.Destroy(instance);
            }
        }

        private static IEnumerator Until(Func<bool> ready, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0) {
            var end = Time.realtimeSinceStartup + 20;
            while (!ready()) {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(end), $"화면 입력 결과 대기 시간 초과 (호출 줄 {line})");
                yield return null;
            }
        }
    }
}
