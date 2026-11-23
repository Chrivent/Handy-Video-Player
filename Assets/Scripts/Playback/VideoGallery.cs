using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using Video = HandyVideoPlayer.Playback.VideoLibrary.Video;

namespace HandyVideoPlayer.Playback {
    internal sealed class VideoGallery : IDisposable {
        private readonly ListView _list;
        private readonly VideoThumbnailLoader _thumbnails;
        private readonly Action<Video> _choose;
        private readonly List<int> _rows = new();
        private readonly HashSet<Tile> _tiles = new();
        private IReadOnlyList<Video> _videos = Array.Empty<Video>();
        private Video _selected;
        private int _columns = 2;

        internal VideoGallery(ListView list, Action<Video> choose) {
            _list = list;
            _thumbnails = new VideoThumbnailLoader();
            _choose = choose;
            _list.makeItem = MakeRow;
            _list.bindItem = BindRow;
            _list.unbindItem = (row, _) => UnbindRow(row);
            _list.destroyItem = DestroyRow;
            _list.itemsSource = _rows;
            _list.RegisterCallback<GeometryChangedEvent>(Resize);
        }

        internal void Show(IReadOnlyList<Video> videos, Video selected) {
            _videos = videos;
            _selected = selected;
            GroupRows();
            _list.RefreshItems();
            if (_rows.Count > 0) _list.ScrollToItem(0);
        }

        internal void Select(Video video) {
            _selected = video;
            foreach (var tile in _tiles) tile.MarkSelected(video);
        }

        private void GroupRows() {
            _rows.Clear();
            for (var i = 0; i < _videos.Count; i += _columns) _rows.Add(i);
        }

        private void Resize(GeometryChangedEvent evt) {
            var width = evt.newRect.width - 18;
            if (width < 40) return;
            var columns = Mathf.Clamp(Mathf.FloorToInt(width / 168), 1, 4);
            var height = Mathf.Clamp(Mathf.RoundToInt(width / columns * 9 / 16) + 33, 95, 192);
            if (!Mathf.Approximately(_list.fixedItemHeight, height)) _list.fixedItemHeight = height;
            if (columns == _columns) return;
            _columns = columns;
            GroupRows();
            _list.Rebuild();
        }

        private VisualElement MakeRow() {
            var row = new VisualElement();
            row.AddToClassList("gallery-row");
            for (var i = 0; i < _columns; i++) {
                var tile = new Tile(_choose);
                _tiles.Add(tile);
                row.Add(tile);
            }
            return row;
        }

        private void BindRow(VisualElement row, int rowIndex) {
            var start = _rows[rowIndex];
            for (var i = 0; i < row.childCount; i++) {
                var tile = (Tile)row[i];
                _ = tile.BindAsync(start + i < _videos.Count ? _videos[start + i] : null, _thumbnails);
                tile.MarkSelected(_selected);
            }
        }

        private static void UnbindRow(VisualElement row) {
            foreach (var child in row.Children()) ((Tile)child).Unbind();
        }

        private void DestroyRow(VisualElement row) {
            UnbindRow(row);
            foreach (var child in row.Children()) _tiles.Remove((Tile)child);
        }

        public void Dispose() {
            _list.UnregisterCallback<GeometryChangedEvent>(Resize);
            foreach (var tile in _tiles) tile.Unbind();
            _thumbnails.Dispose();
            _list.itemsSource = null;
            _list.bindItem = null;
            _list.unbindItem = null;
            _list.destroyItem = null;
            _list.makeItem = null;
            _tiles.Clear();
        }

        private sealed class Tile : Button {
            private readonly Image _image = new() { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            private readonly Label _caption = new() { enableRichText = false, pickingMode = PickingMode.Ignore };
            private readonly Label _missing = new("스크립트 없음") { pickingMode = PickingMode.Ignore };
            private readonly Label _unavailable = new("미리보기 없음") { pickingMode = PickingMode.Ignore };
            private CancellationTokenSource _load;
            private Texture2D _texture;
            private Video _video;

            internal Tile(Action<Video> choose) {
                AddToClassList("gallery-tile");
                var frame = new VisualElement { pickingMode = PickingMode.Ignore };
                frame.AddToClassList("tile-frame");
                _image.AddToClassList("tile-image");
                _missing.AddToClassList("tile-missing");
                _unavailable.AddToClassList("tile-unavailable");
                _caption.AddToClassList("tile-caption");
                frame.Add(_image);
                frame.Add(_unavailable);
                frame.Add(_missing);
                Add(frame);
                Add(_caption);
                clicked += () => { if (_video != null) choose(_video); };
            }

            internal void MarkSelected(Video video) => EnableInClassList("selected", _video != null && _video == video);

            internal async Task BindAsync(Video video, VideoThumbnailLoader thumbnails) {
                var cancellation = CancellationToken.None;
                try {
                    CancellationTokenSource load = null;
                    try {
                        visible = video != null;
                        focusable = video != null;
                        if (_video == video) return;
                        Unbind();
                        if (video == null) return;
                        _video = video;
                        _caption.text = Path.GetFileNameWithoutExtension(video.Path);
                        tooltip = video.DisplayName;
                        _missing.style.display = video.ScriptPath == null ? DisplayStyle.Flex : DisplayStyle.None;
                        load = new CancellationTokenSource();
                        cancellation = load.Token;
                        _load = load;
                        var bytes = await thumbnails.ReadAsync(video, cancellation);
                        if (cancellation.IsCancellationRequested) return;
                        if (bytes != null) {
                            _texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
                            if (_texture.LoadImage(bytes, true)) {
                                _image.image = _texture;
                                return;
                            }
                        }
                        ClearPreview(showUnavailable: true);
                    } catch (OperationCanceledException) when (cancellation.IsCancellationRequested) {
                        // 스크롤이나 검색으로 칸이 재사용되면 다음 영상이 표시를 이어받는다.
                    } finally {
                        if (_load == load) _load = null;
                        load?.Dispose();
                    }
                } catch (Exception error) {
                    Debug.LogException(error);
                    if (cancellation.IsCancellationRequested || video == null || _video != video) return;
                    ClearPreview(showUnavailable: true);
                }
            }

            internal void Unbind() {
                _load?.Cancel();
                _load = null;
                _video = null;
                ClearPreview(showUnavailable: false);
            }

            private void ClearPreview(bool showUnavailable) {
                _image.image = null;
                if (_texture) UnityEngine.Object.Destroy(_texture);
                _texture = null;
                _unavailable.style.display = showUnavailable ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }
    }
}
