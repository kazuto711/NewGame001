namespace Matoi.UI
{
    /// <summary>ゲームを止めるメニューが開いているか。2 つの画面が同時に開かないようにする。</summary>
    public static class MenuState
    {
        public static bool IsOpen;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() => IsOpen = false;
    }
}
