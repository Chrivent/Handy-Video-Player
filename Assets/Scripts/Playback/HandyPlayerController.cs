using HandyDeviceControl.UI;
using HandyDeviceControl;
using UnityEngine;
using UnityEngine.UIElements;

namespace HandyVideoPlayer.Playback {
    [RequireComponent(typeof(UIDocument), typeof(HandyConnectionPanel)), DefaultExecutionOrder(100)]
    public sealed class HandyPlayerController : MonoBehaviour {
        [SerializeField, Tooltip("영상과 같은 이름의 스크립트를 검색할 기본 폴더")]
        private string mediaFolder = @"E:\보안";

        private HandyConnectionPanel _connection;
        private VideoPlaybackController _media;
        private VisualElement _workspace;
        private Button _libraryTab, _settingsTab, _expand;

        private void OnEnable() {
            var root = GetComponent<UIDocument>().rootVisualElement;
            _connection = GetComponent<HandyConnectionPanel>();
            _workspace = root.Q("workspace");
            _libraryTab = root.Q<Button>("library-tab");
            _settingsTab = root.Q<Button>("settings-tab");
            _expand = root.Q<Button>("media-expand");
            _libraryTab.clicked += ShowLibrary;
            _settingsTab.clicked += ShowSettings;
            _expand.clicked += ToggleExpanded;
            _workspace.RegisterCallback<GeometryChangedEvent>(Resize);
            _workspace.RegisterCallback<KeyDownEvent>(OnViewKey, TrickleDown.TrickleDown);

            _media = new VideoPlaybackController(transform, root, mediaFolder, () => _connection.Session);
            _connection.Changed += Render;
            _connection.StopRequested += _media.Rewind;
            _media.Refresh();
            Render();
        }

        private void OnDisable() {
            _libraryTab.clicked -= ShowLibrary;
            _settingsTab.clicked -= ShowSettings;
            _expand.clicked -= ToggleExpanded;
            _workspace.UnregisterCallback<GeometryChangedEvent>(Resize);
            _workspace.UnregisterCallback<KeyDownEvent>(OnViewKey, TrickleDown.TrickleDown);
            _connection.Changed -= Render;
            _connection.StopRequested -= _media.Rewind;
            _media.Dispose();
            _media = null;
        }

        private void Resize(GeometryChangedEvent evt) => _workspace.EnableInClassList("stacked", evt.newRect.width < 900);
        private void ShowLibrary() => _workspace.RemoveFromClassList("settings-open");
        private void ShowSettings() => _workspace.AddToClassList("settings-open");

        private void ToggleExpanded() {
            var expanded = !_workspace.ClassListContains("expanded");
            _workspace.EnableInClassList("expanded", expanded);
            _expand.text = expanded ? "기본 보기" : "크게 보기";
            _expand.Focus();
        }

        private void OnViewKey(KeyDownEvent evt) {
            if (evt.keyCode != KeyCode.Escape || !_workspace.ClassListContains("expanded")) return;
            ToggleExpanded();
            evt.StopPropagation();
        }

        private void Update() => _media?.Tick();
        private void Render() => _media?.Render(_connection.Session?.State ?? HandySessionState.Idle);
    }
}
