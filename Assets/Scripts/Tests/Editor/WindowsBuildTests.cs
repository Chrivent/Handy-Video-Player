using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build.Profile;

namespace HandyVideoPlayer.Tests.Editor {
    public sealed class WindowsBuildTests {
        [Test] public void WindowsProfileSelectsThePlayerScene() {
            var profile = AssetDatabase.LoadAssetAtPath<BuildProfile>("Assets/Data/BuildProfiles/HandyPlayer.asset");
            Assert.That(profile, Is.Not.Null);
            Assert.That(profile.overrideGlobalScenes, Is.True);
            var scenes = profile.GetScenesForBuild().Where(scene => scene.enabled).Select(scene => scene.path).ToList();
            var expected = new[] { "VideoPlayer/HandyPlayer" };
            Assert.That(scenes, Is.EqualTo(expected.Select(name => $"Assets/Scenes/{name}.unity")));
            foreach (var scene in scenes)
                Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(scene), Is.Not.Null);
        }
    }
}
