using UnityEngine;
using UnityEngine.UI;

namespace HandyVideoPlayer.Playback {
    public sealed class PlayerPaneLayout : LayoutGroup {
        public RectTransform sidebar, viewer;
        [Min(1)] public float stackedBelow = 900, minStackedHeight = 560, maxSidebarWidth = 590;
        [Range(0.1f, 0.9f)] public float sidebarWidth = 0.37f, sidebarHeight = 0.45f;
        [Min(0)] public float gap = 12;

        public override void CalculateLayoutInputVertical() { }
        public override void SetLayoutHorizontal() => Arrange(0);
        public override void SetLayoutVertical() => Arrange(1);

        private void Arrange(int axis) {
            if (!sidebar || !viewer) return;
            var size = rectTransform.rect.size;
            // 낮은 가로 창에서는 목록 위의 입력란이 썸네일을 밀어내지 않도록 좌우 배치를 유지한다.
            var stacked = size.x < stackedBelow && size.y >= minStackedHeight;
            var sideBySideAxis = stacked ? 1 : 0;
            var side = axis == sideBySideAxis
                ? (stacked ? Mathf.Max(260f, size.y * sidebarHeight) : Mathf.Min(maxSidebarWidth, size.x * sidebarWidth))
                : size[axis];
            var offset = 0f;
            if (sidebar.gameObject.activeSelf) {
                SetChildAlongAxis(sidebar, axis, 0, side);
                if (axis == sideBySideAxis) offset = side + gap;
            }
            SetChildAlongAxis(viewer, axis, offset, Mathf.Max(0, size[axis] - offset));
        }
    }
}
