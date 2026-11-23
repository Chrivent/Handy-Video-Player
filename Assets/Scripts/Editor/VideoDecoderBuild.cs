using System.Linq;
using HandyVideoPlayer.Playback;
using UnityEditor;
using UnityEditor.Build;

namespace HandyVideoPlayer.Editor {
    internal sealed class VideoDecoderBuild : BuildPlayerProcessor {
        public override void PrepareForBuild(BuildPlayerContext context) {
            var options = context.BuildPlayerOptions;
            if (options.target != BuildTarget.StandaloneWindows64 || options.scenes?.Contains("Assets/Scenes/VideoPlayer/HandyPlayer.unity") != true) return;
            foreach (var name in new[] { VideoDecoderPackage.LibraryName, "LICENSE.LGPL" })
                context.AddAdditionalPathToStreamingAssets(VideoDecoderPackage.EditorFile(name), VideoDecoderPackage.Folder + "/" + name);
            foreach (var name in new[] { "NOTICE.txt", "Copyright", "COPYING", "COPYING.LESSER" })
                context.AddAdditionalPathToStreamingAssets(VideoDecoderPackage.DistributionFile(name), VideoDecoderPackage.Folder + "/" + name);
        }
    }
}
