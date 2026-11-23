using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HandyDeviceControl;
using HandyDeviceControl.Tests.Editor;
using HandyVideoPlayer.Playback;
using NUnit.Framework;

namespace HandyVideoPlayer.Tests.Editor {
    public sealed class HandyScriptPlaybackTests : HandyApiTestFixture {
        [TestCase(false)]
        [TestCase(true)]
        public void VideoScriptWaitsForItsFirstActionAndStopEndsTheSameTransport(bool buffered) {
            const string point = "{\"actions\":[{\"at\":1000,\"pos\":20},{\"at\":1800,\"pos\":80}]}";
            var file = Path.GetTempFileName();
            try {
                File.WriteAllText(file, point);
                var script = HandyMotionScript.Load(file);
                using var client = new HandyApiClient(buffered ? "app" : "", "device", Http);
                Assert.Throws<InvalidOperationException>(() => client.PlayScriptAsync(script, () => 0, CancellationToken.None,
                    () => Assert.Fail("연결 확인 전에 영상이 시작되었습니다.")).GetAwaiter().GetResult());
                if (buffered) {
                    Server.Expect("GET", V3 + "connected", V3Online);
                    Server.Expect("GET", V3 + "info", "{\"result\":{\"fw_version\":\"4.4.0\",\"fw_status\":1}}");
                } else ExpectFirmware3();
                client.CheckConnectionAsync(CancellationToken.None).GetAwaiter().GetResult();
                Task sent;
                if (buffered) {
                    Server.Expect("PUT", V3 + "hsp/setup", "{\"result\":{}}", "{}");
                    Server.Expect("PUT", V3 + "hsp/add", "{\"result\":{}}", "{\"points\":[{\"t\":1000,\"x\":20},{\"t\":1800,\"x\":80}]}");
                    sent = Server.Expect("PUT", V3 + "hsp/play", "{\"result\":{\"play_state\":1}}", checkBody: body => {
                        var match = Regex.Match(body, "^\\{\"start_time\":(\\d+),\"playback_rate\":1,\"loop\":false\\}$");
                        Assert.That(match.Success);
                        Assert.That(int.Parse(match.Groups[1].Value), Is.InRange(1000, 1400));
                    });
                    Server.Expect("PUT", V3 + "mode", "{\"result\":\"ok\"}", "{\"mode\":0}");
                    Server.Expect("PUT", V3 + "hamp/stop", "{\"result\":{}}");
                    Server.Expect("GET", V3 + "hamp/state", "{\"result\":{\"play_state\":0}}");
                } else {
                    Server.Expect("PUT", V2 + "mode", Success, "{\"mode\":2}");
                    sent = Server.Expect("PUT", V2 + "hdsp/xpt", Success, "{\"position\":20,\"duration\":320,\"stopOnTarget\":true,\"immediateResponse\":false}");
                    ExpectV2Stop();
                }
                var clock = new TestVideoClock { TimeMs = 100 };
                var playback = client.PlayScriptAsync(script, clock.Read, CancellationToken.None);
                Assert.That(sent.Wait(100), Is.False);
                clock.TimeMs = 1000;
                Assert.That(sent.Wait(3000), Is.True);
                client.StopAsync().GetAwaiter().GetResult();
                Assert.Catch<OperationCanceledException>(() => playback.GetAwaiter().GetResult());
                Server.Verify();
            } finally { File.Delete(file); }
        }

        [Test] public void StoppingDuringStreamSetupKeepsVideoPausedAndSendsNoPoints() {
            var file = Path.GetTempFileName();
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            try {
                File.WriteAllText(file, "{\"actions\":[{\"at\":0,\"pos\":50}]}");
                using var client = new HandyApiClient("app", "device", Http);
                Server.Expect("GET", V3 + "connected", V3Online);
                Server.Expect("GET", V3 + "info", "{\"result\":{\"fw_version\":\"4.4.0\",\"fw_status\":0}}");
                client.CheckConnectionAsync(CancellationToken.None).GetAwaiter().GetResult();
                var setup = Server.Expect("PUT", V3 + "hsp/setup", "{\"result\":{}}", "{}", release.Task);
                Server.Expect("PUT", V3 + "mode", "{\"result\":\"ok\"}", "{\"mode\":0}");
                Server.Expect("PUT", V3 + "hamp/stop", "{\"result\":{}}");
                Server.Expect("GET", V3 + "hamp/state", "{\"result\":{\"play_state\":0}}");
                var started = 0;
                var playback = client.PlayScriptAsync(HandyMotionScript.Load(file), () => 0, CancellationToken.None,
                    () => Interlocked.Increment(ref started));
                Assert.That(setup.Wait(2000));
                var stop = client.StopAsync();
                Assert.That(stop.IsCompleted, Is.False);
                release.TrySetResult(true);
                stop.GetAwaiter().GetResult();
                Assert.Catch<OperationCanceledException>(() => playback.GetAwaiter().GetResult());
                Assert.That(started, Is.Zero);
                Server.Verify();
            } finally {
                release.TrySetResult(true);
                File.Delete(file);
            }
        }

        private sealed class TestVideoClock {
            internal volatile int TimeMs;
            internal int Read() => TimeMs;
        }
    }
}
