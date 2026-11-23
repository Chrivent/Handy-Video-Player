using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace HandyVideoPlayer.Playback {
    internal sealed class VideoThumbnailLoader : IDisposable {
        private const int CacheSize = 80;
        private readonly Dictionary<VideoLibrary.Video, byte[]> _images = new();
        private readonly Queue<VideoLibrary.Video> _order = new();
        private readonly SemaphoreSlim _workers = new(2);
        private readonly HashSet<VideoDecoder> _active = new();
        private bool _closed;

        internal async Task<byte[]> ReadAsync(VideoLibrary.Video video, CancellationToken cancellation) {
            if (_images.TryGetValue(video, out var image)) return image;
            await _workers.WaitAsync(cancellation);
            try {
                cancellation.ThrowIfCancellationRequested();
                if (_closed) return null;
                if (_images.TryGetValue(video, out image)) return image;
                image = await ReadFrameAsync(video.Path, cancellation);
                cancellation.ThrowIfCancellationRequested();
                if (!_images.ContainsKey(video)) {
                    if (_order.Count >= CacheSize) _images.Remove(_order.Dequeue());
                    _order.Enqueue(video);
                }
                _images[video] = image;
                return image;
            } finally { _workers.Release(); }
        }

        private async Task<byte[]> ReadFrameAsync(string path, CancellationToken cancellation) {
            VideoDecoder player = null;
            try {
                player = new VideoDecoder(path, thumbnail: true);
                _active.Add(player);
                var timer = Stopwatch.StartNew();
                while (!_closed && timer.ElapsedMilliseconds < 8000) {
                    cancellation.ThrowIfCancellationRequested();
                    player.Tick(256, 144);
                    if (player.Ready) return player.Picture.EncodeToJPG(80);
                    await Task.Delay(30, cancellation);
                }
                return null;
            } catch (Exception error) when (error is IOException or InvalidDataException or System.ComponentModel.Win32Exception
                or DllNotFoundException or PlatformNotSupportedException) {
                return null;
            } finally {
                if (player != null && _active.Remove(player)) player.Dispose();
            }
        }

        public void Dispose() {
            _closed = true;
            // 에디터가 재생 모드를 끝내면 대기 중인 코드가 다음 프레임에 실행되지 않을 수 있다.
            foreach (var player in _active) player.Dispose();
            _active.Clear();
        }
    }
}
