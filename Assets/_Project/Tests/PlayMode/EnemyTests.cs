// M7 動作確認：敵の索敵・追跡・ため・攻撃、被弾反応・死亡、プレイヤーの死亡と復活、同士討ちなし。
using System.Collections;
using System.IO;
using System.Linq;
using Matoi.AI;
using Matoi.Characters;
using Matoi.Combat;
using Matoi.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Matoi.Tests
{
    public class EnemyTests : InputTestFixture
    {
        const float CamYaw = 20f;
        Gamepad pad;
        GameObject player;
        CharacterMotor motor;
        Health playerHealth;
        WeaponHolder holder;
        WeaponShooter shooter;

        IEnumerator LoadField()
        {
            pad = InputSystem.AddDevice<Gamepad>();
            SceneManager.LoadScene("TestField");
            yield return null;
            yield return null;
            player = GameObject.FindWithTag("Player");
            motor = player.GetComponent<CharacterMotor>();
            playerHealth = player.GetComponent<Health>();
            holder = player.GetComponent<WeaponHolder>();
            shooter = player.GetComponent<WeaponShooter>();
            Time.timeScale = 1f;
            yield return new WaitForSeconds(0.4f);
        }

        static Vector2 StickFor(Vector3 worldDir)
        {
            worldDir.y = 0;
            var l = Quaternion.Euler(0, -CamYaw, 0) * worldDir.normalized;
            return new Vector2(l.x, l.z);
        }

        EnemyBrain EnemyAt(int i) => EnemyBrain.All.OrderBy(e => e.name).ElementAt(i);

        [UnityTest]
        public IEnumerator Enemy_Detects_Chases_WindsUp_AndHitsPlayer()
        {
            yield return LoadField();
            Assert.GreaterOrEqual(EnemyBrain.All.Count, 4, "敵が配置されていない");
            var e = EnemyAt(0);
            // かかし（的）が間に入らない、道路の開けた場所で行う
            motor.Teleport(new Vector3(1.5f, 0.1f, -2f), 0f);
            yield return null;
            e.PlaceAt(player.transform.position + Vector3.forward * 9f, 180f);
            bool chased = false, shotWindup = false;
            float t = 0f, firstHit = -1f;
            while (t < 9f && firstHit < 0f)
            {
                chased |= e.Current == EnemyBrain.State.Chase;
                if (!shotWindup && e.Current == EnemyBrain.State.Windup)
                {
                    shotWindup = true;
                    Shot("M7_enemy_windup");
                    CloseUp(e.transform, "M7_enemy_windup_close");
                }
                if (playerHealth.Current < playerHealth.Max) firstHit = t;
                t += Time.deltaTime;
                yield return null;
            }
            Debug.Log($"[MatoiTest] enemy: chased={chased} windups={e.WindupCount} firstHitAt={firstHit:0.00}s playerHP={playerHealth.Current}");
            Assert.IsTrue(chased, "プレイヤーに気づいて追いかけていない");
            Assert.GreaterOrEqual(e.WindupCount, 1, "攻撃の前の「ため」が無い");
            Assert.Greater(firstHit, 0f, "プレイヤーに攻撃が当たっていない");
            Assert.AreEqual(playerHealth.Max - e.Definition.attackDamage, playerHealth.Current, 0.01f);
        }

        [UnityTest]
        public IEnumerator Enemy_StaggersAndDies_FromRifleFire()
        {
            yield return LoadField();
            var e = EnemyAt(0);
            motor.Teleport(new Vector3(1.5f, 0.1f, -2f), 0f);
            yield return null;
            e.PlaceAt(player.transform.position + Vector3.forward * 11f, 180f);
            yield return Tap(pad.rightShoulder); // ライフルへ
            var bar = e.GetComponent<FloatingHealthBar>();
            bool staggered = false, barShown = false, flashShot = false;
            float t = 0f;
            Press(pad.rightTrigger);
            while (t < 4f && !e.Health.IsDead)
            {
                Set(pad.rightStick, StickFor(e.transform.position - player.transform.position));
                staggered |= e.Current == EnemyBrain.State.Stagger;
                barShown |= bar && bar.IsVisible;
                if (!flashShot && e.Health.Current < e.Health.Max) { flashShot = true; CloseUp(e.transform, "M7_enemy_hit"); }
                t += Time.deltaTime;
                yield return null;
            }
            Release(pad.rightTrigger);
            Set(pad.rightStick, Vector2.zero);
            yield return new WaitForSeconds(1.2f);
            var anim = e.GetComponentInChildren<Animator>();
            CloseUp(e.transform, "M7_enemy_dead");
            Shot("M7_enemy_dead_game");
            Debug.Log($"[MatoiTest] kill: dead={e.Health.IsDead} t={t:0.00}s staggered={staggered} bar={barShown} animDead={anim.GetBool("Dead")} shots={shooter.ShotsFired}");
            Assert.IsTrue(e.Health.IsDead, "ライフルで倒せていない");
            Assert.IsTrue(staggered, "被弾でひるんでいない");
            Assert.IsTrue(barShown, "体力バーが表示されていない");
            Assert.IsTrue(anim.GetBool("Dead"), "死亡アニメーションになっていない");
            Assert.AreEqual(EnemyBrain.State.Dead, e.Current);
        }

        [UnityTest]
        public IEnumerator Melee_KnocksEnemyBack()
        {
            yield return LoadField();
            var e = EnemyAt(0);
            Vector3 front = player.transform.position + player.transform.forward * 1.5f;
            e.PlaceAt(front, player.transform.eulerAngles.y + 180f);
            yield return null;
            Vector3 before = e.transform.position;
            yield return Tap(pad.buttonEast, 0.6f);
            float moved = Vector3.Distance(Flat(before), Flat(e.transform.position));
            Vector3 away = Flat(e.transform.position - before);
            float dirOk = Vector3.Dot(away.normalized, Flat(player.transform.forward));
            Debug.Log($"[MatoiTest] melee enemy: hp={e.Health.Current} moved={moved:0.00} dirDot={dirOk:0.00}");
            Assert.AreEqual(e.Health.Max - player.GetComponent<MeleeAttack>().damage, e.Health.Current, 0.01f);
            Assert.Greater(moved, 0.4f, "殴った敵が吹き飛んでいない");
            Assert.Greater(dirOk, 0.5f, "吹き飛ぶ向きがおかしい");
        }

        [UnityTest]
        public IEnumerator Player_DiesAndRespawns()
        {
            yield return LoadField();
            Vector3 spawn = player.transform.position;
            motor.Teleport(spawn + Vector3.forward * 5f, 0f);
            playerHealth.TakeDamage(new DamageInfo { amount = 999f, direction = Vector3.back, knockback = 2f });
            yield return new WaitForSeconds(1.0f);
            var anim = player.GetComponentInChildren<Animator>();
            bool deadAnim = anim.GetBool("Dead");
            Shot("M7_player_dead");
            Assert.IsTrue(playerHealth.IsDead);
            yield return new WaitForSeconds(3.0f);
            float distToSpawn = Vector3.Distance(Flat(player.transform.position), Flat(spawn));
            Debug.Log($"[MatoiTest] respawn: deadAnim={deadAnim} alive={!playerHealth.IsDead} hp={playerHealth.Current} distToSpawn={distToSpawn:0.00}");
            Assert.IsTrue(deadAnim, "死亡アニメーションになっていない");
            Assert.IsFalse(playerHealth.IsDead, "復活していない");
            Assert.AreEqual(playerHealth.Max, playerHealth.Current);
            Assert.Less(distToSpawn, 0.5f, "スタート地点に戻っていない");
        }

        [UnityTest]
        public IEnumerator Enemies_DoNotHurtEachOther()
        {
            yield return LoadField();
            var a = EnemyAt(1);
            var b = EnemyAt(2);
            Vector3 p = new Vector3(-20f, 0.2f, 30f);
            a.PlaceAt(p, 0f);
            b.PlaceAt(p + Vector3.forward * 1.4f, 180f);
            yield return null;
            a.GetComponent<MeleeAttack>().TryAttack(Vector3.forward);
            yield return new WaitForSeconds(0.6f);
            Debug.Log($"[MatoiTest] friendly fire: bHP={b.Health.Current}");
            Assert.AreEqual(b.Health.Max, b.Health.Current, 0.01f, "敵どうしで攻撃が当たっている");
        }

        // ---------- ヘルパー ----------
        IEnumerator Tap(UnityEngine.InputSystem.Controls.ButtonControl btn, float after = 0.3f)
        {
            Press(btn); yield return null; yield return null; Release(btn);
            yield return new WaitForSeconds(after);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

        void CloseUp(Transform t, string name)
        {
            var go = new GameObject("TempCloseUp");
            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(Camera.main);
            cam.fieldOfView = 30f;
            Vector3 mid = (t.position + player.transform.position) * 0.5f;
            Vector3 across = Vector3.Cross(Vector3.up, Flat(t.position - player.transform.position).normalized);
            float span = Vector3.Distance(t.position, player.transform.position);
            go.transform.position = mid + across * (3.5f + span * 0.8f) + Vector3.up * 2.2f;
            go.transform.LookAt(mid + Vector3.up * 0.9f);
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
