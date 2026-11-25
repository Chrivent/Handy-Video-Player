using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Video = HandyVideoPlayer.Playback.VideoLibrary.Video;

namespace HandyVideoPlayer.Playback {
    internal sealed class VideoGallery : IDisposable {
        private readonly ScrollRect _list;
        private readonly VideoGalleryViewport _viewport;
        private readonly VideoTile _prefab;
        private readonly Action<Video> _choose;
        private readonly VideoThumbnailLoader _thumbnails = new();
        private readonly List<VideoTile> _pool = new();
        private IReadOnlyList<Video> _videos = Array.Empty<Video>();
        private Video _selected;
        private Vector2 _lastSize;
        private int _first = -1;
        private const float Gap = 4;

        internal VideoGallery(ScrollRect list, VideoTile prefab, Action<Video> choose) {
            _list = list;
            _viewport = list.viewport.GetComponent<VideoGalleryViewport>();
            _prefab = prefab;
            _choose = choose;
            _list.onValueChanged.AddListener(Scrolled);
            _viewport.LayoutRequested += UpdateLayout;
        }

        internal void Show(IReadOnlyList<Video> videos, Video selected) {
            _videos = videos;
            _selected = selected;
            _list.StopMovement();
            _list.content.anchoredPosition = Vector2.zero;
            _first = -1;
            UpdateLayout();
        }

        internal void Select(Video video) {
            if (_selected == video) return;
            _selected = video;
            foreach (var tile in _pool) tile.SetSelection(video);
        }

        private void Scrolled(Vector2 _) => _viewport.RequestLayout();

        private void UpdateLayout() {
            if (!_list.gameObject.activeInHierarchy) return;
            var size = _list.viewport.rect.size;
            if (size.x < 1 || size.y < 1) return;
            var columns = Mathf.Clamp(Mathf.FloorToInt(size.x / 168), 1, 4);
            var width = size.x / columns;
            var height = Mathf.Clamp(Mathf.Round(width * 9 / 16) + 33, 95, 192);
            var rows = Mathf.CeilToInt((float)_videos.Count / columns);
            var visibleRows = Mathf.CeilToInt(size.y / height) + 1;
            var firstRow = Mathf.Clamp(Mathf.FloorToInt(_list.content.anchoredPosition.y / height), 0, Mathf.Max(0, rows - visibleRows));
            var first = columns * firstRow;
            if (_lastSize == size && _first == first) return;
            _lastSize = size;
            _first = first;
            _list.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, rows * height);
            var count = Mathf.Min(_videos.Count - first, visibleRows * columns);
            while (_pool.Count < count) {
                var tile = UnityEngine.Object.Instantiate(_prefab, _list.content, false);
                tile.Initialize(_choose);
                _pool.Add(tile);
            }
            for (var i = 0; i < _pool.Count; i++) {
                var tile = _pool[i];
                if (i >= count) {
                    if (tile.gameObject.activeSelf) {
                        tile.Unbind();
                        tile.gameObject.SetActive(false);
                    }
                    continue;
                }
                if (!tile.gameObject.activeSelf) tile.gameObject.SetActive(true);
                var rect = (RectTransform)tile.transform;
                var index = first + i;
                rect.anchoredPosition = new Vector2((index % columns) * width + Gap / 2, -(index / columns) * height - Gap / 2);
                rect.sizeDelta = new Vector2(width - Gap, height - Gap);
                tile.Bind(_videos[index], _selected, _thumbnails);
            }
        }

        public void Dispose() {
            _list.onValueChanged.RemoveListener(Scrolled);
            _viewport.LayoutRequested -= UpdateLayout;
            foreach (var tile in _pool) {
                tile.Unbind();
                tile.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(tile.gameObject);
            }
            _pool.Clear();
            _thumbnails.Dispose();
        }
    }
}
