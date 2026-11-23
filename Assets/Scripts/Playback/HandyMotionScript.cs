using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;
using HandyDeviceControl;

namespace HandyVideoPlayer.Playback {
    public sealed class HandyMotionScript {
        [DataContract]
        private struct FilePoint {
            [DataMember(Name = "at", IsRequired = true)] public int TimeMs { get; private set; }
            [DataMember(Name = "pos", IsRequired = true)] public int Position { get; private set; }
        }

        [DataContract]
        private sealed class FileData {
            [DataMember(Name = "actions", IsRequired = true)] public FilePoint[] Actions { get; set; }
            [DataMember(Name = "inverted")] public bool Inverted { get; set; }
        }

        private readonly List<HandyMotionPoint> _points;
        public int Count => _points.Count;
        public HandyMotionPoint this[int index] => _points[index];
        public int FirstTimeMs => _points[0].TimeMs;
        public int LastTimeMs => _points[Count - 1].TimeMs;

        private HandyMotionScript(List<HandyMotionPoint> points) => _points = points;

        public static HandyMotionScript Load(string path, CancellationToken cancellation = default) {
            using var input = File.OpenRead(path);
            cancellation.ThrowIfCancellationRequested();
            var serializer = new DataContractJsonSerializer(typeof(FileData));
            var data = (FileData)serializer.ReadObject(input);
            if (data?.Actions == null || data.Actions.Length == 0)
                throw new InvalidDataException("스크립트에 재생할 위치가 없습니다.");
            var points = new List<HandyMotionPoint>(data.Actions.Length);
            foreach (var action in data.Actions.OrderBy(point => point.TimeMs)) {
                cancellation.ThrowIfCancellationRequested();
                if (action.TimeMs < 0 || action.Position is < 0 or > 100)
                    throw new InvalidDataException("스크립트의 시간 또는 위치가 범위를 벗어났습니다.");
                var point = new HandyMotionPoint(action.TimeMs, data.Inverted ? 100 - action.Position : action.Position);
                // 같은 시각이 두 번 기록되면 파일에서 나중에 지정한 위치를 쓴다.
                if (points.Count > 0 && points[^1].TimeMs == point.TimeMs) points[^1] = point;
                else points.Add(point);
            }
            return new HandyMotionScript(points);
        }

        internal int FirstAfter(int timeMs) {
            var low = 0;
            var high = Count;
            while (low < high) {
                var middle = low + (high - low) / 2;
                if (_points[middle].TimeMs <= timeMs) low = middle + 1;
                else high = middle;
            }
            return low;
        }

        public int PositionAt(int timeMs) {
            var next = FirstAfter(timeMs);
            if (next == 0) return _points[0].Position;
            var from = _points[next - 1];
            if (next == Count) return from.Position;
            var to = _points[next];
            var fraction = (double)(timeMs - from.TimeMs) / (to.TimeMs - from.TimeMs);
            return (int)Math.Round(from.Position + (to.Position - from.Position) * fraction);
        }
    }
}
