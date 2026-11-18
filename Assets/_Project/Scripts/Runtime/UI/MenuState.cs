namespace Matoi.UI
{
    /// <summary>ゲームを止めるメニューが開いているか。2 つの画面が同時に開かないようにする。</summary>
    public static class MenuState
    {
        static bool isOpen;

        public static bool IsOpen
        {
            get => isOpen;
            set
            {
                if (isOpen && !value) ClosedFrame = UnityEngine.Time.frameCount;
                isOpen = value;
            }
        }

        /// <summary>最後にメニューを閉じたフレーム（同じボタンで別の画面が開かないように使う）。</summary>
        public static int ClosedFrame { get; private set; } = -1;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay()
        {
            isOpen = false;
            ClosedFrame = -1;
        }
    }
}
