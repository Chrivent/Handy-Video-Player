using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace HandyVideoPlayer.Playback {
    public sealed class VideoGalleryViewport : UIBehaviour {
        internal event Action LayoutRequested;
        private bool _pending;

        protected override void OnEnable() => RequestLayout();
        protected override void OnRectTransformDimensionsChange() => RequestLayout();
        protected override void OnDisable() => CancelRequest();

        internal void RequestLayout() {
            if (_pending || !isActiveAndEnabled) return;
            // 같은 프레임의 스크롤과 크기 변경은 Canvas가 배치를 마친 뒤 한 번만 적용한다.
            _pending = true;
            Canvas.willRenderCanvases += NotifyLayout;
        }

        private void NotifyLayout() {
            CancelRequest();
            LayoutRequested?.Invoke();
        }

        private void CancelRequest() {
            if (!_pending) return;
            Canvas.willRenderCanvases -= NotifyLayout;
            _pending = false;
        }
    }
}
