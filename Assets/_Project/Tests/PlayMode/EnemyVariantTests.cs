// 敵の種類の確認：疾駆体の速さ、重殻体の硬さ、吐瘴体の遠距離攻撃、ウェーブへの混ざり方。
using System.Collections;
using System.IO;
using System.Linq;
using Matoi.AI;
using Matoi.CameraSystem;
using Matoi.Characters;
using Matoi.Combat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Matoi.Tests
{
    public class EnemyVariantTests : InputTestFixture
    {
        const string Basic = "侵食者", Fast = "疾駆体", Heavy = "重殻体", Ranged = "吐瘴体";
        static readonly Vector3 Arena = new Vector3(1.5f, 0.1f, -2f);

        GameObject player;
        CharacterMotor motor;
        Health playerHealth;

        IEnumerator LoadField()
        {
            InputSystem.AddDevice<Gamepad>();
            SceneManager.LoadScene("TestField");
            yield return null;
            yield return null;
            Object.FindAnyObjectByType<CameraModeSwitcher>().SetMode(CameraMode.TopDown);
            player = GameObject.FindWithTag("Player");
            motor = player.GetComponent<CharacterMotor>();
            playerHealth = player.GetComponent<Health>();
            motor.Teleport(Arena, 0f);
            yield return new WaitForSeconds(0.3f);
        }

        /// <summary>指定した種類の敵を 1 体だけ残す。</summary>
        EnemyBrain Only(string displayName)
        {
            var enemies = EnemyBrain.All.OrderBy(e => e.name).ToArray();
            var chosen = enemies.First(e => e.Definition.displayName == displayName);
            foreach (var e in enemies) if (e != chosen) e.gameObject.SetActive(false);
            return chosen;
        }

        static DamageInfo Hit(float amount, bool melee) =>
            new DamageInfo { amount = amount, point = Vector3.zero, direction = Vector3.forward, isMelee = melee };

        [UnityTest]
        public IEnumerator Field_HasEveryEnemyType_OnWalkableGround()
        {
            yield return LoadField();
            foreach (string type in new[] { Basic, Fast, Heavy, Ranged })
            {
                var found = EnemyBrain.All.Where(e => e.Definition.displayName == type).ToArray();
                Assert.Greater(found.Length, 0, $"{type}がフィールドにいない");
                foreach (var e in found)
                    Assert.IsTrue(NavMesh.SamplePosition(e.transform.position, out _, 2f, NavMesh.AllAreas), $"{e.name}が歩けない場所にいる");
            }
        }

        [UnityTest]
        public IEnumerator Fast_ClosesTheDistanceQuickly_ButDiesEasily()
        {
            yield return LoadField();
            var fast = Only(Fast);
            fast.PlaceAt(Arena + Vector3.forward * 11f, 180f);
            float start = Time.time, top = 0f;
            var enemyMotor = fast.GetComponent<CharacterMotor>();
            while (Time.time < start + 5f && Vector3.Distance(fast.transform.position, player.transform.position) > 2.2f)
            {
                top = Mathf.Max(top, enemyMotor.Speed);
                yield return null;
            }
            float took = Time.time - start;
            Debug.Log($"[MatoiTest] fast enemy: reached in {took:0.00}s top={top:0.00}");
            Assert.Less(took, 3.2f, "疾駆体が距離を詰めるのが遅い");
            Assert.Greater(top, 5.2f, "疾駆体が基本の敵より速くない");
            Assert.Less(fast.Health.Max, 90f, "疾駆体の体力が基本の敵より高い");
        }

        [UnityTest]
        public IEnumerator Heavy_ResistsGuns_ButTakesFullMeleeDamage()
        {
            yield return LoadField();
            var heavy = Only(Heavy);
            heavy.PlaceAt(Arena + Vector3.forward * 20f, 180f);
            yield return null;
            float max = heavy.Health.Current;
            Assert.Greater(max, 200f, "重殻体が硬くない");
            heavy.Health.TakeDamage(Hit(50f, false));
            Assert.AreEqual(max - 30f, heavy.Health.Current, 0.01f, "銃のダメージが軽減されていない");
            heavy.Health.TakeDamage(Hit(50f, true));
            Assert.AreEqual(max - 80f, heavy.Health.Current, 0.01f, "近接のダメージまで軽減されている");
        }

        [UnityTest]
        public IEnumerator Ranged_KeepsItsDistance_AndHitsAStandingPlayer()
        {
            yield return LoadField();
            var ranged = Only(Ranged);
            ranged.PlaceAt(Arena + Vector3.forward * 10f, 180f);
            float end = Time.time + 5f, closest = float.MaxValue;
            bool shot = false;
            while (Time.time < end && playerHealth.Current >= playerHealth.Max)
            {
                closest = Mathf.Min(closest, Vector3.Distance(ranged.transform.position, player.transform.position));
                if (!shot && ranged.Current == EnemyBrain.State.RangedWindup && Object.FindAnyObjectByType<EnemyProjectile>()) shot = true;
                yield return null;
            }
            Debug.Log($"[MatoiTest] ranged enemy: shots={ranged.ShotsFired} closest={closest:0.0} hp={playerHealth.Current}");
            Assert.IsTrue(shot, "弾をためていない");
            Assert.GreaterOrEqual(ranged.ShotsFired, 1, "弾を撃っていない");
            Assert.Greater(closest, 5f, "吐瘴体が近づきすぎている");
            Assert.AreEqual(playerHealth.Max - ranged.Definition.projectileDamage, playerHealth.Current, 0.01f, "弾が当たっていない");
        }

        [UnityTest]
        public IEnumerator Ranged_ShotCanBeDodgedBySteppingAside()
        {
            yield return LoadField();
            var ranged = Only(Ranged);
            ranged.PlaceAt(Arena + Vector3.forward * 12f, 180f);
            yield return new WaitUntil(() => ranged.ShotsFired >= 1);
            motor.Teleport(player.transform.position + Vector3.right * 2.5f, 0f);   // 放たれた直後に横へよける
            yield return new WaitForSeconds(1.4f);
            Assert.AreEqual(playerHealth.Max, playerHealth.Current, 0.01f, "横へよけても当たった");
        }

        [UnityTest]
        public IEnumerator Ranged_BacksAwayWhenThePlayerGetsClose()
        {
            yield return LoadField();
            var ranged = Only(Ranged);
            ranged.PlaceAt(Arena + Vector3.forward * 3.2f, 180f);
            // 弾を撃っている間は下がらないので、撃ち終わるまで待ってから比べる
            yield return new WaitUntil(() => ranged.ShotsFired >= 1);
            float before = Vector3.Distance(ranged.transform.position, player.transform.position);
            yield return new WaitForSeconds(0.9f);
            float after = Vector3.Distance(ranged.transform.position, player.transform.position);
            Debug.Log($"[MatoiTest] ranged retreat: {before:0.0} -> {after:0.0}");
            Assert.Greater(after, before + 0.5f, "近づかれても後ずさらない");
        }

        [UnityTest]
        public IEnumerator Ranged_LosesTheChargedShot_WhenStaggered()
        {
            yield return LoadField();
            var ranged = Only(Ranged);
            ranged.PlaceAt(Arena + Vector3.forward * 10f, 180f);
            yield return new WaitUntil(() => ranged.Current == EnemyBrain.State.RangedWindup);
            yield return null;
            ranged.Health.TakeDamage(new DamageInfo { amount = 10f, point = ranged.transform.position, direction = Vector3.forward, knockback = 2f, source = player });
            yield return new WaitForSeconds(0.15f);
            Assert.AreEqual(0, ranged.ShotsFired, "ひるんだのに弾を撃った");
            Assert.IsNull(Object.FindAnyObjectByType<EnemyProjectile>(), "ためていた弾が消えていない");
        }

        [UnityTest]
        public IEnumerator Waves_MixInSpecialEnemies_FromTheSecondWave()
        {
            yield return LoadField();
            foreach (var e in EnemyBrain.All.ToArray()) e.gameObject.SetActive(false);
            var director = Object.FindAnyObjectByType<WaveDirector>();
            director.StartWaves();
            yield return null;
            Assert.IsTrue(director.AliveEnemies.All(e => e.Definition.displayName == Basic), "第 1 ウェーブに特殊な敵がいる");
            director.KillAllForTest();
            yield return new WaitUntil(() => director.Wave == 2);
            yield return null;
            int specials = director.AliveEnemies.Count(e => e.Definition.displayName != Basic);
            Debug.Log($"[MatoiTest] wave 2: {director.AliveEnemies.Count} enemies, specials={specials}");
            Assert.Greater(specials, 0, "第 2 ウェーブに特殊な敵が混ざっていない");
            director.KillAllForTest();
            playerHealth.ResetHealth();
        }

        [UnityTest]
        public IEnumerator LineUpShot()
        {
            yield return LoadField();
            var types = new[] { Basic, Fast, Heavy, Ranged };
            var enemies = types.Select(t => EnemyBrain.All.OrderBy(e => e.name).First(e => e.Definition.displayName == t)).ToArray();
            foreach (var e in EnemyBrain.All.ToArray()) if (!enemies.Contains(e)) e.gameObject.SetActive(false);
            motor.Teleport(new Vector3(40f, 0.1f, 40f), 0f);
            Vector3 center = new Vector3(3f, 0.1f, 4f);
            for (int i = 0; i < enemies.Length; i++) enemies[i].PlaceAt(center + Vector3.right * ((i - 1.5f) * 2f), 180f);
            yield return new WaitForSeconds(0.6f);

            var go = new GameObject("TempShot");
            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(Camera.main);
            cam.fieldOfView = 42f;
            go.transform.position = center + new Vector3(0f, 1.7f, -8f);
            go.transform.LookAt(center + Vector3.up * 1.1f);
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            cam.targetTexture = null;
            RenderTexture.active = null;
            string dir = Path.Combine(Application.dataPath, "../Logs/Shots");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "ENEMY_lineup.png"), tex.EncodeToPNG());
            Object.Destroy(tex);
            rt.Release();
            Object.Destroy(go);
        }
    }
}
