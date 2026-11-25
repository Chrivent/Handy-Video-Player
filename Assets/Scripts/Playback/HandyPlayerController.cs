using HandyDeviceControl;
using HandyDeviceControl.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace HandyVideoPlayer.Playback {
    [RequireComponent(typeof(HandyPlayerView), typeof(HandyConnectionCanvas)), DefaultExecutionOrder(100)]
    public sealed class HandyPlayerController : MonoBehaviour {
        [SerializeField, Tooltip("영상과 같은 이름의 스크립트를 검색할 기본 폴더")]
        private string mediaFolder = @"E:\보안";

        private HandyConnectionCanvas _connection;
        private HandyPlayerView _view;
        private VideoPlaybackController _media;
        private Color _libraryColor, _settingsColor, _libraryText, _settingsText;
        private bool _expanded;

        private void Awake() {
            _view = GetComponent<HandyPlayerView>();
            _connection = GetComponent<HandyConnectionCanvas>();
            _libraryColor = _view.libraryTab.image.color;
            _settingsColor = _view.settingsTab.image.color;
            _libraryText = _view.libraryTabText.color;
            _settingsText = _view.settingsTabText.color;
        }

        private void OnEnable() {
            _view.libraryTab.onClick.AddListener(ShowLibrary);
            _view.settingsTab.onClick.AddListener(ShowSettings);
            _view.expand.onClick.AddListener(ToggleExpanded);
            SetExpanded(false);
            ShowLibrary();
            _media = new VideoPlaybackController(_view, mediaFolder, () => _connection.Session);
            _connection.Changed += Render;
            _connection.StopRequested += _media.Rewind;
            _media.Refresh();
            Render();
        }

        private void OnDisable() {
            _view.libraryTab.onClick.RemoveListener(ShowLibrary);
            _view.settingsTab.onClick.RemoveListener(ShowSettings);
            _view.expand.onClick.RemoveListener(ToggleExpanded);
            _connection.Changed -= Render;
            _connection.StopRequested -= _media.Rewind;
            _media.Dispose();
            _media = null;
        }

        private void ShowLibrary() => ShowTab(false);
        private void ShowSettings() => ShowTab(true);
        private void ShowTab(bool settings) {
            _view.library.SetActive(!settings);
            _view.settings.SetActive(settings);
            _view.libraryTab.image.color = settings ? _libraryColor : _view.activeTab;
            _view.settingsTab.image.color = settings ? _view.activeTab : _settingsColor;
            _view.libraryTabText.color = settings ? _libraryText : _view.activeTabText;
            _view.settingsTabText.color = settings ? _view.activeTabText : _settingsText;
        }

        private void SetExpanded(bool value) {
            _expanded = value;
            _view.heading.SetActive(!value);
            _view.sidebar.SetActive(!value);
            _view.expandLabel.text = value ? "기본 보기" : "크게 보기";
        }

        private void ToggleExpanded() {
            SetExpanded(!_expanded);
            if (EventSystem.current) _view.expand.Select();
        }

        private void Update() {
            if (_expanded && Keyboard.current?.escapeKey.wasPressedThisFrame == true) ToggleExpanded();
            _media?.Tick();
        }

        private void Render() => _media?.Render(_connection.Session?.State ?? HandySessionState.Idle);
    }
}
