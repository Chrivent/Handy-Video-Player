using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace HandyVideoPlayer.Playback {
    public sealed class VideoLibrary {
        public sealed class Video {
            public string Path { get; }
            public string DisplayName { get; }
            public string ScriptPath { get; }
            internal Video(string path, string displayName, string scriptPath) {
                Path = path;
                DisplayName = displayName;
                ScriptPath = scriptPath;
            }
        }

        private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase) {
            ".mp4", ".m4v", ".mov", ".wmv", ".avi", ".webm", ".mkv"
        };
        public List<Video> Videos { get; } = new();
        public int Paired { get; private set; }
        public int SkippedFolders { get; private set; }

        public static VideoLibrary Scan(string folder, CancellationToken cancellation = default) {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                throw new DirectoryNotFoundException("영상 폴더를 찾을 수 없습니다. 경로를 확인하세요.");
            var root = new DirectoryInfo(folder);
            var prefix = root.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length + 1;
            var folders = new Stack<DirectoryInfo>();
            folders.Push(root);
            var library = new VideoLibrary();
            while (folders.Count > 0) {
                cancellation.ThrowIfCancellationRequested();
                var current = folders.Pop();
                try {
                    foreach (var entry in current.EnumerateFileSystemInfos()) {
                        cancellation.ThrowIfCancellationRequested();
                        if (entry is DirectoryInfo child) {
                            if ((child.Attributes & FileAttributes.ReparsePoint) == 0) folders.Push(child);
                        } else if (VideoExtensions.Contains(entry.Extension)) {
                            var path = entry.FullName;
                            var script = Path.ChangeExtension(path, ".funscript");
                            if (File.Exists(script)) library.Paired++;
                            else script = null;
                            library.Videos.Add(new Video(path, path[prefix..], script));
                        }
                    }
                } catch (Exception error) when (!ReferenceEquals(current, root) && error is IOException or UnauthorizedAccessException) {
                    library.SkippedFolders++;
                }
            }
            library.Videos.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.DisplayName, b.DisplayName));
            return library;
        }
    }
}
