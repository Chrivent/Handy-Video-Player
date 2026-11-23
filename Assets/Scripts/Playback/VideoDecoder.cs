using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HandyVideoPlayer.Playback {
    internal sealed class VideoDecoder : IDisposable {
        private IntPtr _handle, _renderer;
        private int _width, _height;
        private bool _loaded, _restarted;
        internal Texture2D Picture { get; private set; }
        internal double Duration { get; private set; }
        internal double Position { get; private set; } = -1;
        internal bool Ready => _restarted && Picture && Position >= 0;
        internal bool Seekable { get; private set; }
        internal bool Playing { get; private set; }
        internal bool Ended { get; private set; }

        internal VideoDecoder(string path, bool thumbnail = false) {
            try {
                MpvNative.Load();
                _handle = MpvNative.Create();
                if (_handle == IntPtr.Zero) throw new InvalidOperationException("영상 디코더를 초기화할 수 없습니다.");
                Option("config", "no");
                Option("load-scripts", "no");
                Option("ytdl", "no");
                Option("access-references", "no");
                Option("audio-file-auto", "no");
                Option("sub-auto", "no");
                Option("vo", "libmpv");
                // 홀수 너비의 색차 샘플도 처리하는 libswscale로 영상과 썸네일을 그린다.
                Option("sws-allow-zimg", "no");
                // Tick에서 표시 비율에 맞춰 만든 텍스처의 마지막 열까지 채운다.
                Option("keepaspect", "no");
                Option("pause", "yes");
                Option("keep-open", "yes");
                if (thumbnail) {
                    Option("audio", "no");
                    Option("start", "10%");
                    Option("vd-lavc-threads", "1");
                } else {
                    Option("hwdec", "auto-copy");
                }
                MpvNative.Check(MpvNative.Initialize(_handle));
                _renderer = MpvNative.CreateRenderer(_handle);
                MpvNative.Command(_handle, "loadfile", Path.GetFullPath(path));
            } catch {
                Dispose();
                throw;
            }
        }

        private void Option(string key, string value) => MpvNative.Check(MpvNative.SetOption(_handle, key, value), key);

        internal void Play() {
            MpvNative.Check(MpvNative.SetProperty(_handle, "pause", "no"));
            Playing = true;
        }

        internal void Pause() {
            MpvNative.Check(MpvNative.SetProperty(_handle, "pause", "yes"));
            Playing = false;
        }

        internal void Seek(double position) {
            MpvNative.Command(_handle, "seek", position.ToString("R", CultureInfo.InvariantCulture), "absolute+exact");
            _restarted = Ended = false;
        }

        internal void Tick(int maxWidth, int maxHeight) {
            MpvNative.Event item;
            while ((item = Marshal.PtrToStructure<MpvNative.Event>(MpvNative.WaitEvent(_handle, 0))).Id != MpvNative.EventId.None) {
                switch (item.Id) {
                    case MpvNative.EventId.FileLoaded:
                        _loaded = true;
                        Duration = MpvNative.Number(_handle, "duration");
                        Seekable = MpvNative.Flag(_handle, "seekable");
                        break;
                    case MpvNative.EventId.VideoReconfigured:
                    case MpvNative.EventId.PlaybackRestart:
                        _width = (int)MpvNative.Number(_handle, "dwidth");
                        _height = (int)MpvNative.Number(_handle, "dheight");
                        if (item.Id == MpvNative.EventId.PlaybackRestart) {
                            if (_width <= 0 || _height <= 0) throw new InvalidDataException("읽을 수 있는 영상 트랙이 없습니다.");
                            if (Duration <= 0) Duration = MpvNative.Number(_handle, "duration");
                            _restarted = true;
                        }
                        break;
                    case MpvNative.EventId.EndFile:
                        var end = Marshal.PtrToStructure<MpvNative.EndFile>(item.Data);
                        MpvNative.Check(end.Error);
                        if (!_loaded || end.Reason != 0) throw new InvalidDataException("재생할 수 있는 로컬 영상이 없습니다.");
                        Ended = true;
                        break;
                }
            }
            if (!_loaded || _width <= 0 || _height <= 0) return;
            var scale = Math.Min(1, Math.Min((double)maxWidth / _width, (double)maxHeight / _height));
            var width = Math.Max(1, (int)Math.Round(_width * scale));
            var height = Math.Max(1, (int)Math.Round(_height * scale));
            var resize = !Picture || Picture.width != width || Picture.height != height;
            if ((MpvNative.UpdateRenderer(_renderer) & 1) != 0 || resize) {
                if (resize) {
                    ReleasePicture();
                    Picture = new Texture2D(width, height, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp };
                }
                MpvNative.Draw(_renderer, Picture);
            }
            Position = MpvNative.Number(_handle, "time-pos");
            if (Playing) Ended |= MpvNative.Flag(_handle, "eof-reached");
        }

        private void ReleasePicture() {
            if (!Picture) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(Picture);
            else UnityEngine.Object.DestroyImmediate(Picture);
            Picture = null;
        }

        public void Dispose() {
            if (_renderer != IntPtr.Zero) { MpvNative.FreeRenderer(_renderer); _renderer = IntPtr.Zero; }
            if (_handle != IntPtr.Zero) { MpvNative.Destroy(_handle); _handle = IntPtr.Zero; }
            ReleasePicture();
        }
    }
}
