using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace HandyVideoPlayer.Playback {
    internal static unsafe class MpvNative {
        private const string Library = VideoDecoderPackage.LibraryName;
        private const CallingConvention C = CallingConvention.Cdecl;
        private static readonly byte[] Software = { (byte)'s', (byte)'w', 0 };
        private static readonly byte[] PixelFormat = System.Text.Encoding.ASCII.GetBytes("rgb24\0");
        private static IntPtr _module;

        internal enum EventId { None = 0, EndFile = 7, FileLoaded = 8, VideoReconfigured = 17, PlaybackRestart = 21 }
        [StructLayout(LayoutKind.Sequential)]
        internal struct Event {
            internal EventId Id;
            private int _error;
            private ulong _request;
            internal IntPtr Data;
        }
        [StructLayout(LayoutKind.Sequential)]
        internal struct EndFile { internal int Reason, Error; }

        // 영상 재생 라이브러리에서 0은 매개변수 배열의 끝을, 1은 렌더러 종류를 나타낸다.
        private enum Parameter { ApiType = 1, BlockForTargetTime = 12, Size = 17, Format, Stride, Pixels }
        [StructLayout(LayoutKind.Sequential)]
        private readonly struct RenderParameter {
            private readonly Parameter _type;
            private readonly void* _value;
            internal RenderParameter(Parameter type, void* value) { _type = type; _value = value; }
        }

        internal static void Load() {
            if (_module != IntPtr.Zero) return;
            if (Application.platform is not (RuntimePlatform.WindowsEditor or RuntimePlatform.WindowsPlayer) || IntPtr.Size != 8)
                throw new PlatformNotSupportedException("영상 재생에는 64비트 Windows가 필요합니다.");
            var path = VideoDecoderPackage.LibraryPath;
            if (!File.Exists(path)) throw new FileNotFoundException("영상 재생 라이브러리를 찾을 수 없습니다. 플레이어 배포 폴더 전체를 확인하세요.", path);
            // 지정된 DLL 폴더와 Windows 시스템 폴더에서만 종속 라이브러리를 찾는다.
            _module = LoadLibraryExW(path, IntPtr.Zero, 0x100 | 0x800);
            if (_module == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "영상 재생 라이브러리를 열 수 없습니다.");
            AppDomain.CurrentDomain.DomainUnload += (_, _) => FreeLibrary(_module);
        }

        internal static void Check(int result, string option = null) {
            if (result >= 0) return;
            var message = Marshal.PtrToStringAnsi(ErrorString(result)) ?? $"디코더 오류 {result}";
            throw new InvalidDataException(option == null ? message : $"{option} · {message}");
        }

        internal static void Command(IntPtr handle, params string[] arguments) {
            var values = stackalloc IntPtr[arguments.Length + 1];
            var count = 0;
            try {
                for (; count < arguments.Length; count++) values[count] = Marshal.StringToCoTaskMemUTF8(arguments[count]);
                values[count] = IntPtr.Zero;
                Check(RunCommand(handle, values));
            } finally {
                for (var i = 0; i < count; i++) Marshal.FreeCoTaskMem(values[i]);
            }
        }

        internal static IntPtr CreateRenderer(IntPtr handle) {
            fixed (byte* api = Software) {
                var options = stackalloc RenderParameter[] { new(Parameter.ApiType, api), default };
                Check(CreateRenderContext(out var renderer, handle, options));
                return renderer;
            }
        }

        internal static void Draw(IntPtr renderer, Texture2D texture) {
            var size = stackalloc int[] { texture.width, texture.height };
            var pitch = (UIntPtr)(uint)(texture.width * 3);
            var block = 0;
            fixed (byte* format = PixelFormat) {
                var options = stackalloc RenderParameter[] {
                    new(Parameter.Size, size), new(Parameter.Format, format), new(Parameter.Stride, &pitch),
                    new(Parameter.Pixels, texture.GetRawTextureData<byte>().GetUnsafePtr()),
                    new(Parameter.BlockForTargetTime, &block), default
                };
                Check(RenderFrame(renderer, options));
            }
            texture.Apply(false, false);
        }

        internal static double Number(IntPtr handle, string name) {
            double value;
            return GetProperty(handle, name, 5, &value) < 0 ? -1 : value;
        }
        internal static bool Flag(IntPtr handle, string name) {
            int value;
            return GetProperty(handle, name, 3, &value) >= 0 && value != 0;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr LoadLibraryExW(string path, IntPtr file, uint flags);
        [DllImport("kernel32.dll")] private static extern bool FreeLibrary(IntPtr module);
        [DllImport(Library, CallingConvention = C, EntryPoint = "mpv_create")] internal static extern IntPtr Create();
        [DllImport(Library, CallingConvention = C, EntryPoint = "mpv_initialize")] internal static extern int Initialize(IntPtr handle);
        [DllImport(Library, CallingConvention = C, EntryPoint = "mpv_set_option_string")] internal static extern int SetOption(IntPtr handle, string key, string value);
        [DllImport(Library, CallingConvention = C, EntryPoint = "mpv_set_property_string")] internal static extern int SetProperty(IntPtr handle, string key, string value);
        [DllImport(Library, CallingConvention = C, EntryPoint = "mpv_get_property")] private static extern int GetProperty(IntPtr handle, string key, int format, void* value);
        [DllImport(Library, CallingConvention = C, EntryPoint = "mpv_command")] private static extern int RunCommand(IntPtr handle, IntPtr* arguments);
        [DllImport(Library, CallingConvention = C, EntryPoint = "mpv_wait_event")] internal static extern IntPtr WaitEvent(IntPtr handle, double timeout);
        [DllImport(Library, CallingConvention = C, EntryPoint = "mpv_error_string")] private static extern IntPtr ErrorString(int code);
        [DllImport(Library, CallingConvention = C, EntryPoint = "mpv_terminate_destroy")] internal static extern void Destroy(IntPtr handle);
        [DllImport(Library, CallingConvention = C, EntryPoint = "mpv_render_context_create")] private static extern int CreateRenderContext(out IntPtr renderer, IntPtr handle, RenderParameter* parameters);
        [DllImport(Library, CallingConvention = C, EntryPoint = "mpv_render_context_update")] internal static extern ulong UpdateRenderer(IntPtr renderer);
        [DllImport(Library, CallingConvention = C, EntryPoint = "mpv_render_context_render")] private static extern int RenderFrame(IntPtr renderer, RenderParameter* parameters);
        [DllImport(Library, CallingConvention = C, EntryPoint = "mpv_render_context_free")] internal static extern void FreeRenderer(IntPtr renderer);
    }
}
