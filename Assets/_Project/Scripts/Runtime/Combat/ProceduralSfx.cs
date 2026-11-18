using System.Collections.Generic;
using UnityEngine;

namespace Matoi.Combat
{
    /// <summary>
    /// 仮の効果音をプログラムで合成する（音素材が入るまでのつなぎ）。
    /// 本番の音素材を入れたら、AudioClip を差し替えるだけでよい。
    /// </summary>
    public static class ProceduralSfx
    {
        static readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();
        const int Rate = 44100;

        public static AudioClip Shot(ShotSound s) => s == ShotSound.Rifle
            ? Get("rifle", 0.35f, 0.55f, 18f, 90f, 0.9f)
            : Get("pistol", 0.22f, 0.45f, 26f, 130f, 0.8f);

        public static AudioClip Punch => Get("punch", 0.18f, 0.25f, 30f, 70f, 0.9f);
        public static AudioClip Whoosh => Get("whoosh", 0.22f, 0.9f, 10f, 0f, 0.35f);
        public static AudioClip Impact => Get("impact", 0.08f, 0.6f, 60f, 200f, 0.5f);
        public static AudioClip Reload => Get("reload", 0.12f, 0.8f, 45f, 900f, 0.4f);
        /// <summary>パワーアーマーの足音（低いドスン）</summary>
        public static AudioClip Stomp => Get("stomp", 0.28f, 0.3f, 16f, 48f, 0.9f);
        /// <summary>パワーアーマーの起動音</summary>
        public static AudioClip PowerUp => Get("powerup", 0.6f, 0.25f, 4f, 160f, 0.6f);

        /// <param name="noiseMix">ノイズの割合（残りは低い「ドン」という正弦波）</param>
        static AudioClip Get(string key, float length, float noiseMix, float decay, float boomHz, float volume)
        {
            if (cache.TryGetValue(key, out var c)) return c;
            int n = (int)(Rate * length);
            var data = new float[n];
            var rng = new System.Random(key.GetHashCode());
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float env = Mathf.Exp(-t * decay);
                float noise = (float)(rng.NextDouble() * 2 - 1);
                lp = Mathf.Lerp(lp, noise, 0.35f);                    // 少しこもらせる
                float boom = boomHz > 0 ? Mathf.Sin(2 * Mathf.PI * boomHz * t * (1f - t * 2f)) : 0f;
                float attack = Mathf.Clamp01(t * 400f);
                data[i] = (lp * noiseMix + boom * (1f - noiseMix)) * env * attack * volume;
            }
            c = AudioClip.Create(key, n, 1, Rate, false);
            c.SetData(data, 0);
            cache[key] = c;
            return c;
        }
    }
}
