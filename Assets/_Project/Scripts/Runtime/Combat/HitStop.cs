using System.Collections;
using UnityEngine;

namespace Matoi.Combat
{
    /// <summary>
    /// ヒットストップ：攻撃が当たった瞬間にゲーム全体をごく短く止め、手応えを出す。
    /// HitStop.Do(0.06f) のように呼ぶ。
    /// </summary>
    public class HitStop : MonoBehaviour
    {
        static HitStop runner;
        static float until;

        public static void Do(float seconds, float timeScale = 0.05f)
        {
            if (runner == null)
            {
                var go = new GameObject("[HitStop]");
                DontDestroyOnLoad(go);
                runner = go.AddComponent<HitStop>();
            }
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
