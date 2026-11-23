using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using NUnit.Framework;
using HandyVideoPlayer.Playback;

namespace HandyVideoPlayer.Tests.Editor {
    public sealed class HandyMediaTests {
        private string _folder;

        [SetUp] public void SetUp() {
            _folder = Path.Combine(Path.GetTempPath(), "HandyPlayer-Media-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
        }

        [TearDown] public void TearDown() => Directory.Delete(_folder, true);

        [Test] public void PairsOnlyAdjacentScriptsAndDistinguishesVideosWithTheSameName() {
            var nested = Directory.CreateDirectory(Path.Combine(_folder, "다른 폴더")).FullName;
            File.WriteAllText(Path.Combine(_folder, "같은 이름 #1.MP4"), "");
            File.WriteAllText(Path.Combine(_folder, "같은 이름 #1.funscript"), "");
            File.WriteAllText(Path.Combine(nested, "같은 이름 #1.mp4"), "");
            File.WriteAllText(Path.Combine(nested, "보조.roll.funscript"), "");
            var result = VideoLibrary.Scan(_folder);
            Assert.That(result.Videos.Count, Is.EqualTo(2));
            Assert.That(result.Paired, Is.EqualTo(1));
            Assert.That(result.Videos.Single(v => v.ScriptPath != null).DisplayName, Is.EqualTo("같은 이름 #1.MP4"));
            Assert.That(result.Videos.Single(v => v.ScriptPath == null).DisplayName, Does.StartWith("다른 폴더"));
            Assert.Throws<OperationCanceledException>(() => VideoLibrary.Scan(_folder, new CancellationToken(true)));
        }

        [Test] public void OrdersInvertedPointsKeepsLastDuplicateAndInterpolatesAtSeek() {
            var path = Path.Combine(_folder, "test.funscript");
            File.WriteAllText(path, "{\"inverted\":true,\"actions\":[{\"at\":2000,\"pos\":90},{\"at\":1000,\"pos\":40},{\"at\":1000,\"pos\":10}]}");
            var script = HandyMotionScript.Load(path);
            Assert.That(script.Count, Is.EqualTo(2));
            Assert.That(script.FirstTimeMs, Is.EqualTo(1000));
            Assert.That(script.LastTimeMs, Is.EqualTo(2000));
            Assert.That(script.PositionAt(1000), Is.EqualTo(90));
            Assert.That(script.PositionAt(1500), Is.EqualTo(50));
            Assert.That(script.PositionAt(2000), Is.EqualTo(10));
        }

        [TestCase("{\"actions\":[]}")]
        [TestCase("{\"actions\":[{\"at\":1,\"pos\":101}]}")]
        [TestCase("{\"actions\":[{\"at\":-1,\"pos\":1}]}")]
        public void RejectsInvalidPositions(string json) {
            var path = Path.Combine(_folder, "test.funscript");
            File.WriteAllText(path, json);
            Assert.Throws<InvalidDataException>(() => HandyMotionScript.Load(path));
        }

        [Test] public void MissingTimestampIsNotATimeZeroCommand() {
            var path = Path.Combine(_folder, "missing.funscript");
            File.WriteAllText(path, "{\"actions\":[{\"pos\":50}]}");
            Assert.Throws<SerializationException>(() => HandyMotionScript.Load(path));
        }
    }
}
