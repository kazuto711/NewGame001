// 第6マイルストーン：3 段コンボ、こぶしが当たる瞬間の同期、方向別の被弾、敵の予備動作を確認する。
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Matoi.AI;
using Matoi.Characters;
using Matoi.Combat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Matoi.Tests
{
    public class MeleeComboTests : InputTestFixture
    {
        const int ActionLayer = 2;
        Gamepad pad;
        CharacterMotor motor;
        MeleeAttack melee;
        WeaponHolder holder;
        Animator animator;

        IEnumerator LoadField()
        {
            pad = InputSystem.AddDevice<Gamepad>();
            SceneManager.LoadScene("TestField");
            yield return null;
            yield return null;
            Object.FindAnyObjectByType<Matoi.CameraSystem.CameraModeSwitcher>()?.SetMode(Matoi.CameraSystem.CameraMode.TopDown);
            var player = GameObject.FindWithTag("Player");
            motor = player.GetComponent<CharacterMotor>();
            melee = player.GetComponent<MeleeAttack>();
            holder = player.GetComponent<WeaponHolder>();
            animator = player.GetComponentInChildren<Animator>();
            motor.Teleport(new Vector3(1.5f, 0.1f, -2f), 270f);
            // 他の敵に邪魔されないよう、遠くへ離しておく
            foreach (var e in EnemyBrain.All.ToArray()) e.PlaceAt(new Vector3(40f, 0.1f, 40f), 0f);
            yield return new WaitForSeconds(0.4f);
        }

        IEnumerator Tap(ButtonControl button, float after = 0.1f)
        {
            Press(button);
            yield return null;
            yield return null;
            Release(button);
            yield return new WaitForSeconds(after);
        }

        [UnityTest]
        public IEnumerator PressingRepeatedly_PlaysThreeStepCombo_EndingWithHeavy()
        {
            yield return LoadField();
            var seen = new List<int>();
            var states = new List<string>();
            bool heavySeen = false;
            float end = Time.time + 2.2f;
            float nextPress = Time.time;
            while (Time.time < end)
            {
                if (Time.time >= nextPress && seen.Count < 3) { Press(pad.buttonEast); yield return null; Release(pad.buttonEast); nextPress = Time.time + 0.22f; }
                if (melee.IsAttacking && (seen.Count == 0 || seen[seen.Count - 1] != melee.CurrentStep))
                    seen.Add(melee.CurrentStep);
                heavySeen |= melee.CurrentIsHeavy;
                foreach (var s in new[] { "Jab", "Punch", "Hook" })
                    if (animator.GetCurrentAnimatorStateInfo(ActionLayer).IsName(s) && !states.Contains(s)) states.Add(s);
                yield return null;
            }
            Debug.Log($"[MatoiTest] combo: steps={string.Join(",", seen)} states={string.Join(",", states)} heavy={heavySeen}");
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, seen, "3 段コンボになっていない");
            CollectionAssert.AreEqual(new[] { "Jab", "Punch", "Hook" }, states, "コンボのアニメーションの順番が違う");
            Assert.IsTrue(heavySeen, "最後の段が強い攻撃になっていない");
        }

        [UnityTest]
        public IEnumerator PunchContact_MatchesTheHitMoment()
        {
            yield return LoadField();
            Transform lHand = animator.GetBoneTransform(HumanBodyBones.LeftHand), rHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            var report = new List<string>();
            float worst = 0f;
            for (int stepIndex = 0; stepIndex < melee.combo.Length; stepIndex++)
            {
                // 前の段をつないで、目的の段まで進める
                for (int i = 0; i <= stepIndex; i++)
                {
                    Press(pad.buttonEast); yield return null; Release(pad.buttonEast);
                    if (i < stepIndex) yield return new WaitUntil(() => melee.IsAttacking && melee.CurrentStep == i && melee.Progress > 0.4f);
                }
                yield return new WaitUntil(() => melee.IsAttacking && melee.CurrentStep == stepIndex);
                float started = Time.time - melee.Progress * melee.combo[stepIndex].duration;
                float best = -9f, bestTime = 0f;
                while (melee.IsAttacking && melee.CurrentStep == stepIndex)
                {
                    Vector3 center = motor.transform.position;
                    float reach = Mathf.Max(Vector3.Dot(lHand.position - center, motor.transform.forward),
                                            Vector3.Dot(rHand.position - center, motor.transform.forward));
                    if (reach > best) { best = reach; bestTime = Time.time - started; }
                    yield return null;
                }
                float hit = melee.combo[stepIndex].hitTime;
                worst = Mathf.Max(worst, Mathf.Abs(bestTime - hit));
                report.Add($"step{stepIndex}: peak={bestTime:0.00}s hit={hit:0.00}s reach={best:0.00}");
                yield return new WaitForSeconds(0.9f);
            }
            Debug.Log("[MatoiTest] punch contact: " + string.Join(" / ", report));
            Assert.Less(worst, 0.09f, "こぶしが伸びきる瞬間と当たり判定がずれている: " + string.Join(" / ", report));
        }

        [UnityTest]
        public IEnumerator EnemyHitFromEachSide_PlaysMatchingReaction()
        {
            yield return LoadField();
            var enemy = EnemyBrain.All.OrderBy(x => x.name).First();
            enemy.PlaceAt(new Vector3(-8f, 0.1f, -2f), 0f);
            enemy.enabled = false;
            yield return new WaitForSeconds(0.3f);
            var health = enemy.GetComponent<Health>();
            var reaction = enemy.GetComponent<HitReaction>();
            var enemyAnimator = enemy.GetComponentInChildren<Animator>();
            reaction.staggerCooldown = 0f;

            var cases = new (string state, Vector3 direction, bool heavy)[]
            {
                ("Hit", -enemy.transform.forward, false),
                ("HitBack", enemy.transform.forward, false),
                ("HitLeft", enemy.transform.right, false),
                ("HitRight", -enemy.transform.right, false),
                ("HitHeavy", -enemy.transform.forward, true),
            };
            foreach (var c in cases)
            {
                health.ResetHealth();
                health.TakeDamage(new DamageInfo { amount = 5f, direction = c.direction, source = motor.gameObject, heavy = c.heavy });
                bool played = false;
                float end = Time.time + 0.4f;
                while (Time.time < end && !played)
                {
                    played = enemyAnimator.GetCurrentAnimatorStateInfo(ActionLayer).IsName(c.state)
                             || enemyAnimator.GetNextAnimatorStateInfo(ActionLayer).IsName(c.state);
                    yield return null;
                }
                Assert.IsTrue(played, c.state + " の被弾アニメーションが再生されていない");
                yield return new WaitForSeconds(1.1f);
            }
        }

        [UnityTest]
        public IEnumerator HeavyHit_StaggersLongerThanNormalHit()
        {
            yield return LoadField();
            var enemy = EnemyBrain.All.OrderBy(x => x.name).First();
            enemy.PlaceAt(new Vector3(-8f, 0.1f, -2f), 0f);
            enemy.enabled = false;
            yield return new WaitForSeconds(0.3f);
            var health = enemy.GetComponent<Health>();
            var reaction = enemy.GetComponent<HitReaction>();
            reaction.staggerCooldown = 0f;

            health.TakeDamage(new DamageInfo { amount = 5f, direction = Vector3.forward, source = motor.gameObject, isMelee = true });
            float normal = reaction.StaggeredUntil - Time.time;
            yield return new WaitForSeconds(1.2f);
            health.TakeDamage(new DamageInfo { amount = 5f, direction = Vector3.forward, source = motor.gameObject, isMelee = true, heavy = true });
            float heavy = reaction.StaggeredUntil - Time.time;
            Debug.Log($"[MatoiTest] stagger: normal={normal:0.00}s heavy={heavy:0.00}s");
            Assert.Greater(heavy, normal * 1.5f, "強い攻撃のひるみが長くなっていない");
        }

        [UnityTest]
        public IEnumerator Enemy_ShowsWindupAnimation_BeforeItsHitLands()
        {
            yield return LoadField();
            var enemy = EnemyBrain.All.OrderBy(x => x.name).First();
            enemy.PlaceAt(motor.transform.position + motor.transform.forward * 1.4f, motor.transform.eulerAngles.y + 180f);
            enemy.ForceAggro();
            var enemyAnimator = enemy.GetComponentInChildren<Animator>();
            var playerHealth = motor.GetComponent<Health>();
            float hp = playerHealth.Current;

            // Mx_EnemyAttack は全長 2.63 秒で、手が最も速く振り抜かれるのは 1.0 秒（38% の地点）
            const float StrikeNormalized = 0.38f;
            float windupAt = -1f, hitAt = -1f, normalizedAtHit = -1f;
            bool shot = false;
            float end = Time.time + 4f;
            while (Time.time < end && hitAt < 0f)
            {
                var info = enemyAnimator.GetCurrentAnimatorStateInfo(ActionLayer);
                if (windupAt < 0f && info.IsName("EnemyAttack")) windupAt = Time.time;
                if (!shot && windupAt > 0f && Time.time > windupAt + 0.3f) { Shot(enemy.transform, "Melee_enemy_windup"); shot = true; }
                if (playerHealth.Current < hp) { hitAt = Time.time; normalizedAtHit = info.normalizedTime; }
                yield return null;
            }
            Debug.Log($"[MatoiTest] enemy windup: lead={(hitAt - windupAt):0.00}s normalizedAtHit={normalizedAtHit:0.00} strike={StrikeNormalized:0.00}");
            Assert.AreEqual(StrikeNormalized, normalizedAtHit, 0.1f, "敵の腕を振り抜く瞬間と、攻撃が当たる瞬間がずれている");
            Assert.Greater(windupAt, 0f, "敵の攻撃アニメーションが再生されていない");
            Assert.Greater(hitAt, 0f, "敵の攻撃が当たっていない");
            Assert.Greater(hitAt - windupAt, 0.4f, "予備動作が短すぎて、避ける余裕がない");
        }

        [UnityTest]
        public IEnumerator RifleDrawn_MeleeStaysSingleShove()
        {
            yield return LoadField();
            yield return Tap(pad.rightShoulder, 0.6f);
            Assert.AreEqual(WeaponStance.Rifle, holder.CurrentWeapon.stance, "前提：ライフルを持っている");
            yield return Tap(pad.buttonEast, 0.15f);
            Assert.AreEqual(-1, melee.CurrentStep, "ライフルの突きがコンボになっている");
        }

        void Shot(Transform subject, string name)
        {
            var go = new GameObject("TempShot");
            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(Camera.main);
            cam.fieldOfView = 36f;
            Vector3 p = subject.position + Vector3.up;
            go.transform.position = p + Quaternion.Euler(0, 50, 0) * subject.forward * 3.2f + Vector3.up * 0.4f;
            go.transform.LookAt(p);
            string dir = Path.Combine(Application.dataPath, "../Logs/Shots");
            Directory.CreateDirectory(dir);
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            cam.targetTexture = null;
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex);
            rt.Release();
            Object.Destroy(go);
        }
    }
}
