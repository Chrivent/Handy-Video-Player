using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using Video = HandyVideoPlayer.Playback.VideoLibrary.Video;

namespace HandyVideoPlayer.Playback {
    public sealed class VideoTile : MonoBehaviour {
        public Button button;
        public RawImage picture;
        public AspectRatioFitter pictureFit;
        public Text caption;
        public GameObject missing, unavailable;
        public Color selectedColor = new(0.19f, 0.33f, 0.32f);
        private Color _normalColor;
        private CancellationTokenSource _load;
        private Texture2D _texture;
        private Video _video;
        private Action<Video> _choose;

        internal void Initialize(Action<Video> choose) {
            _choose = choose;
            _normalColor = button.image.color;
            button.onClick.AddListener(Choose);
        }

        private void Choose() { if (_video != null) _choose(_video); }

        internal void SetSelection(Video selected) {
            button.image.color = _video != null && _video == selected ? selectedColor : _normalColor;
        }

        internal void Bind(Video video, Video selected, VideoThumbnailLoader loader) {
            if (_video != video) {
                Unbind();
                _video = video;
                caption.text = Path.GetFileNameWithoutExtension(video.Path);
                missing.SetActive(video.ScriptPath == null);
                _ = LoadPictureAsync(video, loader);
            }
            SetSelection(selected);
        }

        private async Task LoadPictureAsync(Video video, VideoThumbnailLoader loader) {
            using var request = new CancellationTokenSource();
            _load = request;
            try {
                var data = await loader.ReadAsync(video, request.Token);
                if (request.IsCancellationRequested) return;
                if (data != null) {
                    _texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
                    if (_texture.LoadImage(data, true)) {
                        pictureFit.aspectRatio = (float)_texture.width / _texture.height;
                        picture.texture = _texture;
                        picture.enabled = true;
                        return;
                    }
                }
                ShowUnavailable();
            } catch (OperationCanceledException) when (request.IsCancellationRequested) {
                // 다른 영상에 재사용되거나 닫힌 칸은 갱신하지 않는다.
            } catch (Exception error) {
                if (request.IsCancellationRequested) return;
                Debug.LogException(error, this);
                ShowUnavailable();
            } finally {
                if (_load == request) _load = null;
            }
        }

        private void ShowUnavailable() {
            ClearPicture();
            unavailable.SetActive(true);
        }

        private void ClearPicture() {
            picture.texture = null;
            picture.enabled = false;
            ReleaseTexture();
        }

        private void ReleaseTexture() {
            if (_texture) Destroy(_texture);
            _texture = null;
        }

        internal void Unbind() {
            _load?.Cancel();
            _load = null;
            _video = null;
            ClearPicture();
            unavailable.SetActive(false);
        }

        private void OnDestroy() {
            _load?.Cancel();
            ReleaseTexture();
        }
    }
}
