#if UNITY_EDITOR_WIN
using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using HandyVideoPlayer.Playback;
using UnityEngine;
using UnityEngine.TestTools;

namespace HandyVideoPlayer.Tests.Editor {
    public sealed class VideoDecoderTests {
        [UnityTest]
        public IEnumerator OpensHevcPausedThenSeeksPlaysAndRewinds() {
            var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
            var path = Path.Combine(folder.FullName, "한글 영상 #1.mp4");
            try {
                File.Copy(Path.Combine(Application.dataPath, "Scripts/Tests/Editor/Fixtures/HevcSample.bytes"), path);
                using var decoder = new VideoDecoder(path);
                yield return Until(decoder, static player => player.Ready);
                Assert.That(decoder.Duration, Is.EqualTo(3).Within(0.1));
                Assert.That(decoder.Playing, Is.False);
                Assert.That(decoder.Picture.GetPixel(40, 8).r, Is.GreaterThan(0.85f));
                Assert.That(decoder.Picture.GetPixel(40, 50).b, Is.GreaterThan(0.85f));
                decoder.Seek(2);
                yield return Until(decoder, static player => player.Ready);
                Assert.That(decoder.Position, Is.EqualTo(2).Within(0.1));
                Assert.That(decoder.Picture.GetPixel(40, 8).g, Is.GreaterThan(0.85f));
                decoder.Play();
                yield return Until(decoder, static player => player.Position > 2.2);
                decoder.Pause();
                decoder.Tick(96, 64);
                var paused = decoder.Position;
                var timer = Stopwatch.StartNew();
                yield return Until(decoder, _ => timer.ElapsedMilliseconds > 200);
                Assert.That(decoder.Position, Is.EqualTo(paused).Within(0.09));
                decoder.Play();
                yield return Until(decoder, static player => player.Ended);
                decoder.Pause();
                decoder.Seek(0);
                yield return Until(decoder, static player => player.Ready);
                Assert.That(decoder.Position, Is.LessThan(0.1));
                Assert.That(decoder.Ended, Is.False);
            } finally {
                folder.Delete(true);
            }
        }

        [UnityTest]
        public IEnumerator ThumbnailUsesThePackagedDecoder() {
            var path = Path.Combine(Application.dataPath, "Scripts/Tests/Editor/Fixtures/HevcSample.bytes");
            using var decoder = new VideoDecoder(path, thumbnail: true);
            yield return Until(decoder, static player => player.Ready, 55, 41);
            Assert.That(decoder.Playing, Is.False);
            Assert.That(decoder.Picture.width, Is.EqualTo(55));
            Assert.That(decoder.Picture.EncodeToJPG().Length, Is.GreaterThan(0));
            Assert.That(decoder.Picture.GetPixel(40, 8).r, Is.GreaterThan(0.85f));
        }

        private static readonly bool[] ThumbnailModes = { false, true };

        [UnityTest]
        public IEnumerator DrawsSubsampledOddWidthFramesOnResize([ValueSource(nameof(ThumbnailModes))] bool thumbnail) {
            var path = Path.Combine(Application.dataPath, "Scripts/Tests/Editor/Fixtures/OddWidth420.bytes");
            using var decoder = new VideoDecoder(path, thumbnail);
            yield return Until(decoder, static player => player.Ready, 256, 144);
            Assert.That(decoder.Picture.width, Is.EqualTo(159));
            Assert.That(decoder.Picture.height, Is.EqualTo(91));
            foreach (var width in new[] { 93, 64, 95, 159 }) {
                decoder.Tick(width, 144);
                var frame = decoder.Picture;
                Assert.That(frame.width, Is.EqualTo(width));
                Assert.That(frame.GetPixel(frame.width - 1, frame.height / 4).r, Is.GreaterThan(0.85f));
                Assert.That(frame.GetPixel(frame.width - 1, frame.height * 3 / 4).b, Is.GreaterThan(0.85f));
            }
        }

        private static IEnumerator Until(VideoDecoder decoder, Func<VideoDecoder, bool> condition, int width = 96, int height = 64) {
            var timer = Stopwatch.StartNew();
            do {
                Assert.That(timer.Elapsed.TotalSeconds, Is.LessThan(12), "영상 상태 변경 시간 초과");
                decoder.Tick(width, height);
                if (condition(decoder)) yield break;
                yield return null;
            } while (true);
        }
    }
}
#endif
