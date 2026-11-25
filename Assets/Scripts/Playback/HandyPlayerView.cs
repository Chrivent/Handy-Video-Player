using UnityEngine;
using UnityEngine.UI;

namespace HandyVideoPlayer.Playback {
    public sealed class HandyPlayerView : MonoBehaviour {
        public GameObject heading, sidebar, library, settings;
        public Button libraryTab, settingsTab, expand;
        public Text expandLabel;
        public Color activeTab = new(0.52f, 0.86f, 0.76f);
        public Color activeTabText = new(0.06f, 0.16f, 0.15f);
        public Text libraryTabText, settingsTabText;

        public InputField folder, filter;
        public Button refresh, play, pause, first;
        public ScrollRect list;
        public CanvasGroup listInput;
        public VideoTile tile;
        public Text count, selected, status, time, empty;
        public RectTransform stage;
        public RawImage picture;
        public AspectRatioFitter pictureFit;
        public Slider seek;
    }
}
