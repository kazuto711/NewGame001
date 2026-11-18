using System.Collections;
using UnityEngine;

namespace Matoi.Combat
{
    /// <summary>
    /// ヒットストップ：攻撃が当たった瞬間に時間をほんの少し止めて、手応えを出す。
    /// HitStop.Do(0.06f) のように呼ぶ。強い攻撃ほど長く、深く止める。
    /// </summary>
    public class HitStop : MonoBehaviour
    {
        static HitStop runner;
        static float until;

        /// <summary>最後に指定された長さ（秒）。</summary>
        public static float LastDuration { get; private set; }
        /// <summary>これまでに発生した回数。</summary>
        public static int Count { get; private set; }

        /// <param name="seconds">止める長さ（秒）</param>
        /// <param name="timeScale">止めている間の時間の速さ（小さいほど深く止まる）</param>
        public static void Do(float seconds, float timeScale = 0.05f)
        {
            if (runner == null)
            {
                var go = new GameObject("[HitStop]");
                DontDestroyOnLoad(go);
                runner = go.AddComponent<HitStop>();
            }
            LastDuration = seconds;
            Count++;
            until = Mathf.Max(until, Time.unscaledTime + seconds);
            Time.timeScale = timeScale;
            runner.StopAllCoroutines();
            runner.StartCoroutine(runner.Restore());
        }

        IEnumerator Restore()
        {
            while (Time.unscaledTime < until) yield return null;
            Time.timeScale = 1f;
        }
    }
}
