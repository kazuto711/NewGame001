using System;
using UnityEngine;

namespace Matoi.Missions
{
    /// <summary>シーンを読み直しても残る、ゲーム全体の流れの情報。</summary>
    public static class GameFlow
    {
        /// <summary>開発用の数値表示を出すか（F9 で切替）。</summary>
        public static bool ShowDevInfo;
        /// <summary>次の読み込みでミッションを最初から始める。</summary>
        public static bool StartMissionOnLoad;
        /// <summary>次の読み込みではタイトル画面を出さない。</summary>
        public static bool SkipTitleOnce;
        /// <summary>テストの実行中（タイトル画面を出さない）。</summary>
        public static bool ForceAutomated;

        public static bool AutomatedRun => ForceAutomated || Application.isBatchMode;

        /// <summary>タイトル画面を出す場面か。</summary>
        public static bool ConsumeShowTitle()
        {
            bool skip = SkipTitleOnce;
            SkipTitleOnce = false;
            return !skip && !AutomatedRun;
        }

        public static bool ConsumeStartMission()
        {
            bool start = StartMissionOnLoad;
            StartMissionOnLoad = false;
            return start;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay()
        {
            ShowDevInfo = false;
            StartMissionOnLoad = false;
            SkipTitleOnce = false;
        }
    }

    /// <summary>「寝床で休んだ」「ボスを倒した」など、ミッションへ知らせる合図。</summary>
    public static class MissionSignals
    {
        public const string Rest = "rest", BossDefeated = "boss_defeated", LampLit = "lamp_lit";

        public static event Action<string> Raised;
        public static void Raise(string signal) => Raised?.Invoke(signal);
    }
}
