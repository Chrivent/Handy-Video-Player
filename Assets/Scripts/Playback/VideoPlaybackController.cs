using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HandyDeviceControl;
using UnityEngine;

namespace HandyVideoPlayer.Playback {
    internal sealed class VideoPlaybackController : IDisposable {
        private readonly Transform _parent;
        private readonly Func<HandySession> _session;
        private readonly HandyPlayerView _view;
        private readonly VideoGallery _gallery;
        private readonly List<VideoLibrary.Video> _visible = new();
        private VideoLibrary _library;
        private VideoLibrary.Video _choice;
        private HandyMotionScript _script;
        private VideoDecoder _player;
        private CancellationTokenSource _scan, _load;
        private bool _closed, _prepared, _playing, _seeking;
        private double? _wantedTime;
        private double _seekTarget;
        private float _seekAfter, _deadline, _nextTimeUpdate;
        private int _clockMs = -1;
        private string _message;
        private HandySessionState State => _session()?.State ?? HandySessionState.Idle;

        internal VideoPlaybackController(HandyPlayerView view, string folder, Func<HandySession> session) {
            _view = view;
            _parent = view.transform;
            _session = session;
            _view.folder.SetTextWithoutNotify(folder);
            _gallery = new VideoGallery(view.list, view.tile, video => _ = SelectVideoAsync(video));
            _view.filter.onValueChanged.AddListener(FilterChanged);
            _view.seek.onValueChanged.AddListener(SeekChanged);
            _view.refresh.onClick.AddListener(Refresh);
            _view.play.onClick.AddListener(Play);
            _view.pause.onClick.AddListener(Pause);
            _view.first.onClick.AddListener(FirstAction);
        }

        internal void Refresh() => _ = RefreshLibraryAsync();

        private async Task RefreshLibraryAsync() {
            try {
                if (_closed || State is not (HandySessionState.Idle or HandySessionState.Ready)) return;
                using var scan = new CancellationTokenSource();
                var cancellation = scan.Token;
                try {
                    _scan?.Cancel();
                    _scan = scan;
                    _library = null;
                    ClearVideo();
                    FilterVideos();
                    _view.count.text = "영상 폴더를 검색하고 있습니다.";
                    var path = _view.folder.text.Trim();
                    var library = await Task.Run(() => VideoLibrary.Scan(path, cancellation), cancellation);
                    if (cancellation.IsCancellationRequested) return;
                    _library = library;
                    FilterVideos();
                } catch (OperationCanceledException) when (cancellation.IsCancellationRequested) {
                    // 새 검색이나 닫힌 화면의 상태를 덮어쓰지 않는다.
                } catch (Exception error) {
                    if (!cancellation.IsCancellationRequested && !_closed)
                        _view.count.text = "영상 목록을 불러올 수 없습니다. " + error.GetBaseException().Message + " 경로를 확인하고 새로고침하세요.";
                } finally {
                    if (_scan == scan) {
                        _scan = null;
                        Render(State);
                    }
                }
            } catch (Exception error) {
                Debug.LogException(error, _parent);
                if (_closed) return;
                _view.count.text = "영상 목록 화면을 갱신하지 못했습니다. 폴더를 확인하고 새로고침하세요.";
            }
        }

        private void FilterChanged(string _) => FilterVideos();

        private void FilterVideos() {
            _visible.Clear();
            if (_library != null) {
                var query = _view.filter.text?.Trim() ?? "";
                _visible.AddRange(_library.Videos.Where(video => video.DisplayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0));
                _view.count.text = $"영상 {_library.Videos.Count}개 · 스크립트 연결 {_library.Paired}개 · 표시 {_visible.Count}개"
                    + (_library.SkippedFolders > 0 ? $" · 읽지 못한 폴더 {_library.SkippedFolders}개" : "");
            }
            if (_choice != null && !_visible.Contains(_choice)) ClearVideo();
            _gallery.Show(_visible, _choice);
            Render(State);
        }

        private async Task SelectVideoAsync(VideoLibrary.Video video) {
            try {
                if (_closed || video == null || _playing || State is not (HandySessionState.Idle or HandySessionState.Ready)) return;
                if (video == _choice && (_player != null || _load != null)) return;
                using var load = new CancellationTokenSource();
                var cancellation = load.Token;
                try {
                    ClearVideo();
                    _load = load;
                    _choice = video;
                    _gallery.Select(video);
                    _view.selected.text = video.DisplayName;
                    if (video.ScriptPath == null) {
                        _message = "같은 폴더에 이 영상과 이름이 일치하는 스크립트가 없습니다.";
                        Render(State);
                        return;
                    }
                    _message = "스크립트를 읽고 영상을 준비하고 있습니다.";
                    Render(State);
                    var script = await Task.Run(() => HandyMotionScript.Load(video.ScriptPath, cancellation), cancellation);
                    if (cancellation.IsCancellationRequested) return;
                    _script = script;
                    OpenSelectedVideo();
                } catch (OperationCanceledException) when (cancellation.IsCancellationRequested) {
                    // 다른 썸네일을 고르면 그 영상이 상태 표시를 이어받는다.
                } catch (Exception error) {
                    if (!cancellation.IsCancellationRequested && !_closed)
                        Fail("영상 또는 스크립트를 열 수 없습니다. " + error.GetBaseException().Message + " 파일을 확인하고 썸네일을 다시 선택하세요.");
                } finally {
                    if (_load == load) _load = null;
                }
            } catch (Exception error) {
                Debug.LogException(error, _parent);
                if (_closed || _choice != video) return;
                Fail("선택한 영상을 준비하지 못했습니다. 썸네일을 다시 선택하세요.");
            }
        }

        private void OpenSelectedVideo() {
            _player = new VideoDecoder(_choice.Path);
            _deadline = Time.realtimeSinceStartup + 20;
        }

        private void Fail(string message) {
            var active = _playing;
            ClearPlayer();
            _message = message;
            if (active) _ = _session()?.StopAsync("영상 재생 오류");
            Render(State);
        }

        private void Play() => _ = PlayAsync();

        private async Task PlayAsync() {
            var player = _player;
            try {
                var session = _session();
                if (_playing || !_prepared || _seeking || _wantedTime.HasValue || session?.State != HandySessionState.Ready) return;
                _clockMs = -1;
                _playing = true;
                _message = null;
                await session.RunScriptAsync(_script, () => Volatile.Read(ref _clockMs), player.Play);
            } catch (Exception error) {
                if (!_closed && _player == player) Fail("영상을 시작할 수 없습니다. " + error.GetBaseException().Message);
            }
        }

        private void Pause() {
            if (_playing) _ = _session()?.StopAsync("영상 일시정지");
        }

        private void PauseVideo() {
            _playing = false;
            try {
                _player?.Pause();
            } catch (Exception error) {
                Fail("영상을 일시정지할 수 없습니다. " + error.GetBaseException().Message);
            }
        }

        internal void Rewind() {
            if (_playing) PauseVideo();
            if (!_prepared) return;
            if (_player.Seekable) { QueueSeek(0); return; }
            try {
                ClearPlayer();
                OpenSelectedVideo();
            } catch (Exception error) {
                Fail("영상을 처음부터 열 수 없습니다. " + error.GetBaseException().Message);
            }
        }

        private void FirstAction() {
            if (_script != null) QueueSeek(_script.FirstTimeMs / 1000.0);
        }

        private void SeekChanged(float seconds) => QueueSeek(seconds);

        private void QueueSeek(double seconds) {
            if (!_prepared || _playing || !_player.Seekable) return;
            // 드래그 중 여러 프레임 탐색 요청이 겹치지 않게 마지막 위치만 적용한다.
            _wantedTime = _player.Duration > 0 ? Math.Clamp(seconds, 0, Math.Max(0, _player.Duration - 0.05)) : Math.Max(0, seconds);
            _view.seek.SetValueWithoutNotify((float)_wantedTime.Value);
            _seekAfter = Time.realtimeSinceStartup + 0.15f;
            _message = null;
            Render(State);
        }

        internal void Tick() {
            if (_player == null) return;
            try {
                TickPlayer();
            } catch (Exception error) {
                Fail("영상을 재생할 수 없습니다. " + error.GetBaseException().Message);
            }
        }

        private void TickPlayer() {
            var now = Time.realtimeSinceStartup;
            var stage = _view.stage.rect;
            var width = stage.width > 0 ? Mathf.Clamp(Mathf.CeilToInt(stage.width), 1, 1920) : 960;
            var height = stage.height > 0 ? Mathf.Clamp(Mathf.CeilToInt(stage.height), 1, 1080) : 540;
            _player.Tick(width, height);
            if ((!_prepared || _seeking) && !_player.Ready) {
                if (now > _deadline) Fail("영상 준비 또는 위치 탐색 시간이 초과되었습니다.");
                return;
            }
            if (!_prepared || _seeking) {
                _prepared = true;
                _seeking = false;
                _view.seek.maxValue = (float)Math.Max(0, _player.Duration);
                _message = null;
                _nextTimeUpdate = 0;
                Render(State);
            }
            if (_view.picture.texture != _player.Picture && _player.Picture) {
                _view.picture.texture = _player.Picture;
                _view.pictureFit.aspectRatio = (float)_player.Picture.width / _player.Picture.height;
            }
            if (_playing && _player.Ended) {
                Rewind();
                _message = "재생이 끝났습니다.";
                _ = _session()?.StopAsync("영상 재생 완료");
                return;
            }
            if (_wantedTime.HasValue && now >= _seekAfter) {
                var target = _wantedTime.Value;
                _wantedTime = null;
                if (Math.Abs(target - _player.Position) > 0.015 || _player.Ended) {
                    _seekTarget = target;
                    _deadline = now + 12;
                    _player.Seek(target);
                    _seeking = true;
                }
                Render(State);
            }
            if (_playing && _player.Playing && _player.Position >= 0)
                Volatile.Write(ref _clockMs, (int)Math.Clamp(_player.Position * 1000, 0, int.MaxValue));
            if (now < _nextTimeUpdate) return;
            _nextTimeUpdate = now + 0.15f;
            var seconds = _wantedTime ?? (_seeking ? _seekTarget : _player.Position);
            if (!_wantedTime.HasValue) _view.seek.SetValueWithoutNotify((float)seconds);
            var length = _player.Duration > 0 ? FormatTime(_player.Duration) : "--:--";
            _view.time.text = $"{FormatTime(seconds)} / {length}   ·   첫 동작 {FormatTime(_script.FirstTimeMs / 1000.0)}   ·   동작 {_script.Count}개";
        }

        private static string FormatTime(double value) {
            var time = TimeSpan.FromSeconds(Math.Max(0, value));
            return time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}" : $"{time.Minutes}:{time.Seconds:00}";
        }

        internal void Render(HandySessionState state) {
            if (_closed) return;
            if (_playing && state is not (HandySessionState.Starting or HandySessionState.Running)) PauseVideo();
            var editable = state is HandySessionState.Idle or HandySessionState.Ready;
            _view.folder.interactable = editable;
            _view.refresh.interactable = editable;
            _view.filter.interactable = editable && _library != null;
            _view.listInput.interactable = _view.listInput.blocksRaycasts = editable && _library != null;
            _view.play.interactable = _prepared && !_seeking && !_wantedTime.HasValue && state == HandySessionState.Ready;
            _view.pause.interactable = _playing;
            var canSeek = editable && _prepared && _player.Seekable;
            _view.seek.interactable = canSeek && _player.Duration > 0;
            _view.first.interactable = canSeek && (_player.Duration <= 0 || _script.FirstTimeMs / 1000.0 < _player.Duration);
            _view.picture.enabled = _prepared;
            _view.empty.gameObject.SetActive(_choice == null);
            _view.status.text = _message ?? state switch {
                HandySessionState.Starting when _playing => "핸디 준비 중",
                _ when _playing => "영상과 스크립트 재생 중",
                _ when _seeking || _wantedTime.HasValue => "재생 위치를 찾고 있습니다.",
                HandySessionState.Ready when _prepared => "재생할 준비가 됐습니다.",
                _ when _prepared => "기기 연결에서 연결 확인을 마친 뒤 재생하세요.",
                _ => ""
            };
        }

        private void ClearPlayer() {
            _playing = _prepared = _seeking = false;
            _wantedTime = null;
            _view.picture.texture = null;
            _player?.Dispose();
            _player = null;
            _view.time.text = "";
            _view.seek.SetValueWithoutNotify(0);
        }

        private void ClearVideo() {
            _load?.Cancel();
            _load = null;
            ClearPlayer();
            _script = null;
            _choice = null;
            _message = null;
            _view.selected.text = "영상 미리보기";
        }

        public void Dispose() {
            _closed = true;
            _scan?.Cancel();
            _scan = null;
            ClearVideo();
            _gallery.Dispose();
            _view.filter.onValueChanged.RemoveListener(FilterChanged);
            _view.seek.onValueChanged.RemoveListener(SeekChanged);
            _view.refresh.onClick.RemoveListener(Refresh);
            _view.play.onClick.RemoveListener(Play);
            _view.pause.onClick.RemoveListener(Pause);
            _view.first.onClick.RemoveListener(FirstAction);
        }
    }
}
