using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace HandyVideoPlayer.Editor {
    public static class WindowsBuilds {
        [MenuItem("HandyPlayer/Windows 빌드")]
        public static void BuildHandyPlayer() {
            const string product = "HandyPlayer";
            var profilePath = $"Assets/Data/BuildProfiles/{product}.asset";
            var profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(profilePath);
            if (!profile) throw new BuildFailedException($"빌드 프로필을 찾을 수 없습니다. {profilePath}");

            var directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", product));
            Directory.CreateDirectory(directory);
            var output = Path.Combine(directory, product + ".exe");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerWithProfileOptions {
                buildProfile = profile,
                locationPathName = output,
                options = BuildOptions.StrictMode
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException($"{product} Windows 빌드 결과 · {report.summary.result}");
            Debug.Log($"Windows 빌드 완료 · {output}");
        }
    }
}
