// M8 動作確認：回避（前転・無敵）、敵の攻撃を回避で避ける、ウェーブ進行、ひるみの間隔、トゥーン表現。
using System.Collections;
using System.IO;
using System.Linq;
using Matoi.AI;
using Matoi.Characters;
using Matoi.Combat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Matoi.Tests
{
    public class CombatLoopTests : InputTestFixture
    {
        const float CamYaw = 20f;
        Gamepad pad;
        GameObject player;
        CharacterMotor motor;
        Health health;
        DodgeRoll dodge;
        Animator animator;

        IEnumerator LoadField()
        {
            pad = InputSystem.AddDevice<Gamepad>();
            SceneManager.LoadScene("TestField");
            yield return null;
            yield return null;
            player = GameObject.FindWithTag("Player");
            motor = player.GetComponent<CharacterMotor>();
            health = player.GetComponent<Health>();
            dodge = player.GetComponent<DodgeRoll>();
            animator = player.GetComponentInChildren<Animator>();
            Time.timeScale = 1f;
            motor.Teleport(new Vector3(1.5f, 0.1f, -2f), 0f); // 開けた道路の上
            yield return new WaitForSeconds(0.4f);
        }

        static Vector2 StickFor(Vector3 worldDir)
        {
            worldDir.y = 0;
            var l = Quaternion.Euler(0, -CamYaw, 0) * worldDir.normalized;
            return new Vector2(l.x, l.z);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

        [UnityTest]
        public IEnumerator Dodge_Rolls_AndIsInvulnerableMidRoll()
        {
            yield return LoadField();
            Vector3 start = player.transform.position;
            Set(pad.leftStick, StickFor(Vector3.right));
            yield return null;
            Press(pad.buttonSouth); yield return null; Release(pad.buttonSouth);
            yield return new WaitForSeconds(0.12f);
            health.TakeDamage(new DamageInfo { amount = 30f, direction = Vector3.left });
            bool rollAnim = animator.GetCurrentAnimatorStateInfo(animator.GetLayerIndex("Action")).IsName("Roll")
                            || animator.GetNextAnimatorStateInfo(animator.GetLayerIndex("Action")).IsName("Roll");
            CloseUp("M8_dodge");
            Set(pad.leftStick, Vector2.zero);
            yield return new WaitForSeconds(0.5f);
            float dist = Vector3.Distance(Flat(start), Flat(player.transform.position));
            float dirDot = Vector3.Dot((Flat(player.transform.position - start)).normalized, Vector3.right);
            Debug.Log($"[MatoiTest] dodge: rolls={dodge.RollCount} dist={dist:0.00} dirDot={dirDot:0.00} hp={health.Current} dodged={health.DodgedHits} anim={rollAnim}");
            Assert.AreEqual(1, dodge.RollCount);
            Assert.That(dist, Is.InRange(2.0f, 4.0f), "回避の移動距離が想定外");
            Assert.Greater(dirDot, 0.9f, "スティックの方向へ転がっていない");
            Assert.AreEqual(health.Max, health.Current, "回避中に無敵になっていない");
            Assert.AreEqual(1, health.DodgedHits);
            Assert.IsTrue(rollAnim, "前転アニメーションが再生されていない");
        }

        [UnityTest]
        public IEnumerator Dodge_AvoidsEnemyAttack_WhenTimedOnWindup()
        {
            yield return LoadField();
            var e = EnemyBrain.All.OrderBy(x => x.name).First();
            e.PlaceAt(player.transform.position + Vector3.forward * 8f, 180f);
            float t = 0f;
            while (e.Current != EnemyBrain.State.Windup && t < 8f) { t += Time.deltaTime; yield return null; }
            Assert.AreEqual(EnemyBrain.State.Windup, e.Current, "敵が攻撃のために入らなかった");
            // 「ため」を見てから横へ回避
            Set(pad.leftStick, StickFor(Vector3.right));
            yield return null;
            Press(pad.buttonSouth); yield return null; Release(pad.buttonSouth);
            Set(pad.leftStick, Vector2.zero);
            yield return new WaitForSeconds(0.9f);
            Debug.Log($"[MatoiTest] dodge vs enemy: hp={health.Current} dodged={health.DodgedHits} windups={e.WindupCount}");
            Assert.AreEqual(health.Max, health.Current, "回避したのに攻撃を受けた");
        }

        [UnityTest]
        public IEnumerator Waves_Spawn_Clear_Heal_AndEscalate()
        {
            yield return LoadField();
            var director = Object.FindAnyObjectByType<WaveDirector>();
            Assert.IsNotNull(director, "WaveDirector がシーンに無い");
            health.TakeDamage(new DamageInfo { amount = 50f });
            director.StartWaves();
            yield return new WaitForSeconds(1.5f);
            int wave1 = director.AliveCount;
            bool allChasing = director.AliveEnemies.All(x => x.Current == EnemyBrain.State.Chase || x.Current == EnemyBrain.State.Windup || x.Current == EnemyBrain.State.Attack);
            Shot("M8_wave1");
            director.KillAllForTest();
            yield return null; yield return null;
            var phase = director.CurrentPhase;
            float healed = health.Current;
            yield return new WaitForSeconds(4.5f);
            Debug.Log($"[MatoiTest] waves: wave1={wave1} chasing={allChasing} phaseAfterClear={phase} hpAfter={healed} wave={director.Wave} alive2={director.AliveCount} kills={director.Kills}");
            Assert.AreEqual(director.SizeOf(1), wave1, "1ウェーブ目の敵の数が違う");
            Assert.IsTrue(allChasing, "出現した敵がプレイヤーを追っていない");
            Assert.AreEqual(WaveDirector.Phase.Break, phase);
            Assert.AreEqual(health.Max - 50f + 35f, healed, 0.01f, "クリア時に回復していない");
            Assert.AreEqual(2, director.Wave);
            Assert.AreEqual(director.SizeOf(2), director.AliveCount, "2ウェーブ目で敵が増えていない");
            Assert.AreEqual(wave1, director.Kills);
        }

        [UnityTest]
        public IEnumerator RifleFire_DoesNotStunLockEnemy()
        {
            yield return LoadField();
            var e = EnemyBrain.All.OrderBy(x => x.name).First();
            e.PlaceAt(player.transform.position + Vector3.forward * 12f, 180f);
            var reaction = e.GetComponent<HitReaction>();
            // ダメージだけ与え続ける（倒さないよう、体力を毎回戻す）
            float t = 0f;
            while (t < 2f)
            {
                e.Health.TakeDamage(new DamageInfo { amount = 5f, direction = Vector3.forward, source = player });
                if (e.Health.Current < 30f) e.Health.Heal(100f);
                t += 0.11f; // 約 9 発/秒
                yield return new WaitForSeconds(0.11f);
            }
            Debug.Log($"[MatoiTest] poise: staggers in 2s = {reaction.StaggerCount}");
            Assert.LessOrEqual(reaction.StaggerCount, 3, "連射でひるみ続けている（ハメ状態）");
            Assert.GreaterOrEqual(reaction.StaggerCount, 1);
        }

        [UnityTest]
        public IEnumerator ToonLook_IsAppliedToCharacters()
        {
            yield return LoadField();
            var renderers = player.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r => r.enabled).ToArray();
            var shaders = renderers.SelectMany(r => r.sharedMaterials).Where(m => m).Select(m => m.shader).Distinct().ToArray();
            Debug.Log($"[MatoiTest] toon: {string.Join(", ", shaders.Select(s => s.name + (s.isSupported ? "" : "(unsupported)")))}");
            Assert.IsTrue(shaders.All(s => s.name == "Matoi/Toon"), "主人公の材質がトゥーンになっていない");
            Assert.IsTrue(shaders.All(s => s.isSupported), "トゥーンシェーダーがこの環境で動かない");
        }

        // ---------- 撮影 ----------
        void CloseUp(string name)
        {
            var go = new GameObject("TempCloseUp");
            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(Camera.main);
            cam.fieldOfView = 32f;
            var p = player.transform.position;
            go.transform.position = p + new Vector3(0.5f, 2.0f, -4.5f);
            go.transform.LookAt(p + Vector3.up * 0.7f);
            Render(cam, name);
            Object.Destroy(go);
        }

        static void Shot(string name) => Render(Camera.main, name);

        static void Render(Camera cam, string name)
        {
            string dir = Path.Combine(Application.dataPath, "../Logs/Shots");
            Directory.CreateDirectory(dir);
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            cam.targetTexture = prev;
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex);
            rt.Release();
        }
    }
}
