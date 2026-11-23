using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using HandyDeviceControl;

namespace HandyVideoPlayer.Playback {
    internal sealed class HandyScriptStream : IHandyMotionSource {
        // 영상이 멈추거나 창이 닫히면 미리 보낸 동작도 짧은 구간 안에서 끝난다.
        private const int BufferMs = 3200;
        private const int RefillMs = 1500;
        private const int MaxPointsPerRequest = 24;
        private readonly HandyMotionScript _script;
        private readonly Func<int> _videoTime;
        private readonly int _intervalMs;
        private readonly Action _ready;

        internal HandyScriptStream(HandyMotionScript script, Func<int> videoTime, int intervalMs, Action ready) {
            _script = script;
            _videoTime = videoTime;
            _intervalMs = intervalMs;
            _ready = ready;
        }

        private sealed class VideoClock {
            private readonly Func<int> _read;
            private readonly Stopwatch _sinceFrame = Stopwatch.StartNew();
            private int _previous = -1;

            internal VideoClock(Func<int> read) => _read = read;

            internal int Read() {
                var now = _read();
                if (now != _previous) {
                    _previous = now;
                    _sinceFrame.Restart();
                } else if (_sinceFrame.ElapsedMilliseconds > (now < 0 ? 12000 : 1600)) {
                    throw new InvalidOperationException("영상 재생 시간이 진행되지 않아 기기 전송을 중단했습니다.");
                }
                return now;
            }
        }

        public async Task SendPositionsAsync(Func<int, int, Task> move, CancellationToken cancellation) {
            cancellation.ThrowIfCancellationRequested();
            _ready?.Invoke();
            var clock = new VideoClock(_videoTime);
            var due = _script.FirstTimeMs;
            var previousPosition = -1;
            var lastSent = new Stopwatch();
            while (true) {
                cancellation.ThrowIfCancellationRequested();
                var now = clock.Read();
                if (now >= due && (!lastSent.IsRunning || lastSent.ElapsedMilliseconds >= _intervalMs)) {
                    if (now > (long)_script.LastTimeMs + 200) return;
                    // 첫 위치를 맞추고, 느린 응답 뒤에는 영상에서 이미 지난 동작을 건너뛴다.
                    var next = _script.FirstAfter(now);
                    var target = now;
                    switch (previousPosition) {
                        case >= 0 when next < _script.Count: {
                            var ahead = Math.Clamp(_script[next].TimeMs - now, _intervalMs, 500);
                            target = (int)Math.Min(_script.LastTimeMs, (long)now + ahead);
                            break;
                        }
                        case >= 0:
                            target = _script.LastTimeMs;
                            break;
                    }
                    var position = _script.PositionAt(target);
                    if (position != previousPosition) {
                        lastSent.Restart();
                        await move(position, Math.Max(_intervalMs, target - now)).ConfigureAwait(false);
                        previousPosition = position;
                    }
                    if (target >= _script.LastTimeMs) return;
                    due = (int)Math.Max(target, Math.Min(int.MaxValue, (long)now + _intervalMs));
                }
                await Task.Delay(15, cancellation).ConfigureAwait(false);
            }
        }

        public async Task SendBufferedAsync(Func<IReadOnlyList<HandyMotionPoint>, Task> append,
            Func<int, bool, Task> setTime, CancellationToken cancellation) {
            cancellation.ThrowIfCancellationRequested();
            _ready?.Invoke();
            var clock = new VideoClock(_videoTime);
            var through = -1;
            var syncAt = 0L;
            var latency = 0;
            while (true) {
                cancellation.ThrowIfCancellationRequested();
                var now = clock.Read();
                if (now >= _script.FirstTimeMs) {
                    if (through < 0 && now > (long)_script.LastTimeMs + 200) return;
                    if (through >= _script.LastTimeMs && now >= through) return;
                    if (through >= 0 && now - through > 750)
                        throw new InvalidOperationException("기기 전송이 영상보다 늦어졌습니다. 일시정지한 위치에서 다시 재생하세요.");
                    if (through < _script.LastTimeMs && through - (long)now < RefillMs) {
                        var start = through < 0 ? now : through;
                        var end = (int)Math.Min(_script.LastTimeMs, (long)now + BufferMs);
                        var points = new List<HandyMotionPoint>(MaxPointsPerRequest);
                        if (through < 0) points.Add(new HandyMotionPoint(now, _script.PositionAt(now)));
                        for (var i = _script.FirstAfter(start); i < _script.Count && _script[i].TimeMs <= end && points.Count < MaxPointsPerRequest; i++)
                            points.Add(_script[i]);
                        if (points.Count < MaxPointsPerRequest && (points.Count == 0 || points[^1].TimeMs < end))
                            points.Add(new HandyMotionPoint(end, _script.PositionAt(end)));
                        if (through < 0 && points.Count == 1)
                            points.Add(new HandyMotionPoint((int)Math.Min(int.MaxValue, (long)now + 100), points[0].Position));
                        var trip = Stopwatch.StartNew();
                        await append(points).ConfigureAwait(false);
                        latency = (int)Math.Min(400, trip.ElapsedMilliseconds / 2);
                        through = points[^1].TimeMs;
                    }
                    if (now >= syncAt) {
                        var sample = clock.Read();
                        await setTime((int)Math.Min(int.MaxValue, (long)sample + latency), syncAt == 0).ConfigureAwait(false);
                        syncAt = (long)sample + 5000;
                    }
                }
                await Task.Delay(25, cancellation).ConfigureAwait(false);
            }
        }
    }

    internal static class HandyScriptPlayback {
        internal static Task PlayScriptAsync(this IHandyClient client, HandyMotionScript script, Func<int> videoTime,
            CancellationToken cancellation, Action ready = null) {
            if (client is not IHandyStreamingClient connection)
                throw new NotSupportedException("이 연결은 위치 전송을 지원하지 않습니다.");
            var source = new HandyScriptStream(script, videoTime, client is HandyBleClient ? 55 : 320, ready);
            return connection.StreamAsync(source, cancellation);
        }

        internal static Task RunScriptAsync(this HandySession session, HandyMotionScript script, Func<int> videoTime, Action playVideo) =>
            session.RunMotionAsync(async (client, ready, cancellation) => {
                await client.PlayScriptAsync(script, videoTime, cancellation, ready).ConfigureAwait(false);
                // 마지막 위치 이후에도 영상이 끝나거나 사용자가 정지할 때까지 같은 연결을 유지한다.
                await Task.Delay(Timeout.Infinite, cancellation).ConfigureAwait(false);
            }, playVideo);
    }
}
