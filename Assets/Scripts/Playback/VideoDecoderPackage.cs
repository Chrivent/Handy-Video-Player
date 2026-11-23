using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using System;
using System.IO.Compression;
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Assembly-CSharp-Editor")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("HandyVideoPlayer.Tests.Editor")]
#endif

namespace HandyVideoPlayer.Playback {
    internal static class VideoDecoderPackage {
        internal const string Folder = "libmpv";
        internal const string LibraryName = "libmpv-2.dll";
        internal static string LibraryPath {
            get {
#if UNITY_EDITOR
                return EditorFile(LibraryName);
#else
                return Path.Combine(Application.streamingAssetsPath, Folder, LibraryName);
#endif
            }
        }

#if UNITY_EDITOR
        private const string Version = "0.42.0";
        private static string SourceFolder => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ThirdParty", Folder));
        internal static string DistributionFile(string name) => Path.Combine(SourceFolder, name);

        internal static string EditorFile(string name) {
            var cache = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", "HandyPlayer", Folder, Version));
            var path = Path.Combine(cache, name);
            if (File.Exists(path)) return path;
            Directory.CreateDirectory(cache);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                using var archive = ZipFile.OpenRead(Path.Combine(SourceFolder, $"libmpv-v{Version}-win-x64-lgpl.zip"));
                var entry = archive.GetEntry(name) ?? throw new InvalidDataException($"재생 라이브러리 구성 파일이 없습니다. {name}");
                entry.ExtractToFile(temporary);
                File.Move(temporary, path);
                return path;
            } finally {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
#endif
    }
}
