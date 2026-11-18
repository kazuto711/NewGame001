// ジャンプ・しゃがみ・ステルス（視野、警戒度、音、隠密攻撃）の動作確認。
using System.Collections;
using System.IO;
using System.Linq;
using Matoi.AI;
using Matoi.CameraSystem;
using Matoi.Characters;
using Matoi.Combat;
using Matoi.Player;
using Matoi.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Matoi.Tests
{
    public class JumpCrouchStealthTests : InputTestFixture
    {
        Gamepad pad;
        GameObject player;
        CharacterMotor motor;
        CharacterController cc;
        Animator animator;
        PlayerStealth stealth;
        StealthIndicator indicator;
        EnemyBrain enemy;

        IEnumerator Load()
        {
            pad = InputSystem.AddDevice<Gamepad>();
            SceneManager.LoadScene("TestField");
            yield return null;
            yield return null;
            Object.FindAnyObjectByType<CameraModeSwitcher>().SetMode(CameraMode.ThirdPerson);
            player = GameObject.FindWithTag("Player");
            motor = player.GetComponent<CharacterMotor>();
            cc = player.GetComponent<CharacterController>();
            animator = player.GetComponentInChildren<Animator>();
            stealth = player.GetComponent<PlayerStealth>();
            indicator = player.GetComponent<StealthIndicator>();
            Time.timeScale = 1f;
            motor.Teleport(new Vector3(1.5f, 0.1f, -2f), 0f);
            var all = EnemyBrain.All.OrderBy(x => x.name).ToArray();   // 先頭は基本の侵食者
            enemy = all[0];
            for (int i = 0; i < all.Length; i++) all[i].PlaceAt(new Vector3(40f + i * 3f, 0.1f, 40f), 0f);
            yield return new WaitForSeconds(0.5f);
        }

        IEnumerator Tap(ButtonControl button, float after = 0.3f)
        {
            Press(button);
            yield return null;
            yield return null;
            Release(button);
            yield return new WaitForSeconds(after);
        }

        IEnumerator Crouch() { yield return Tap(pad.dpad.left, 0.4f); Assert.IsTrue(motor.IsCrouching, "前提：しゃがんでいる"); }

        bool Base(string state) => animator.GetCurrentAnimatorStateInfo(0).IsName(state) || animator.GetNextAnimatorStateInfo(0).IsName(state);

        // ---------- ジャンプ ----------
        [UnityTest]
        public IEnumerator Jump_RisesAboutOneMeter_PlaysAnimations_AndLands()
        {
            yield return Load();
            float ground = motor.transform.position.y, top = ground;
            bool start = false, loop = false, land = false;
            Press(pad.buttonSouth);
            yield return null;
            Release(pad.buttonSouth);
            float end = Time.time + 1.6f;
            bool apexShot = false;
            float maxHipsAboveBody = 0f;
            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            while (Time.time < end)
            {
                top = Mathf.Max(top, motor.transform.position.y);
                maxHipsAboveBody = Mathf.Max(maxHipsAboveBody, hips.position.y - motor.transform.position.y);
                if (!apexShot && motor.IsAirborne && motor.Velocity.y < 0.5f) { Shot("Stealth_jump_apex"); apexShot = true; }
                start |= Base("JumpStart");
                loop |= Base("JumpLoop");
                land |= Base("JumpLand");
                yield return null;
            }
            float height = top - ground;
            Debug.Log($"[MatoiTest] jump: height={height:0.00} start={start} loop={loop} land={land} grounded={motor.IsGrounded} hipsAboveBody={maxHipsAboveBody:0.00}");
            Assert.Less(maxHipsAboveBody, 1.25f, "ジャンプのアニメーションで体が余分に浮いている");
            Assert.IsTrue(loop, "空中のアニメーションが再生されていない");
            Assert.That(height, Is.InRange(0.85f, 1.3f), "ジャンプの高さが想定外");
            Assert.IsTrue(start && land, "ジャンプのアニメーションが再生されていない");
            Assert.IsTrue(motor.IsGrounded, "着地していない");
            Assert.AreEqual(ground, motor.transform.position.y, 0.08f, "元の高さに戻っていない");
            Assert.IsTrue(Base("Locomotion"), "着地の後、移動の状態に戻っていない");
        }

        [UnityTest]
        public IEnumerator RunningJump_KeepsMomentum()
        {
            yield return Load();
            var l = Quaternion.Euler(0, -motor.transform.eulerAngles.y, 0) * Vector3.forward;
            Set(pad.leftStick, new Vector2(l.x, l.z));
            yield return new WaitForSeconds(0.7f);
            float speed = motor.Speed;
            Vector3 from = motor.transform.position;
            Press(pad.buttonSouth);
            yield return null;
            Release(pad.buttonSouth);
            Set(pad.leftStick, Vector2.zero);       // 空中で手を離しても、すぐには止まらない
            yield return new WaitUntil(() => motor.IsAirborne);
            yield return new WaitForSeconds(0.45f);
            float air = motor.Speed;
            Debug.Log($"[MatoiTest] running jump: run={speed:0.00} inAir={air:0.00} moved={Vector3.Distance(from, motor.transform.position):0.00}");
            Assert.Greater(air, speed * 0.4f, "空中で勢いが失われている");
        }

        // ---------- しゃがみ ----------
        [UnityTest]
        public IEnumerator Crouch_LowersBody_SlowsDown_AndPlaysCrouchAnimation()
        {
            yield return Load();
            float stand = cc.height;
            yield return Crouch();
            yield return new WaitForSeconds(0.4f);
            Assert.Less(cc.height, stand * 0.75f, "しゃがんでも当たり判定が低くなっていない");
            Assert.IsTrue(Base("Crouch"), "しゃがみのアニメーションになっていない");
            Shot("Stealth_crouch_idle");

            var l = Quaternion.Euler(0, -motor.transform.eulerAngles.y, 0) * Vector3.forward;
            Set(pad.leftStick, new Vector2(l.x, l.z));
            yield return new WaitForSeconds(0.9f);
            float speed = motor.Speed;
            Shot("Stealth_crouch_walk");
            Set(pad.leftStick, Vector2.zero);
            Debug.Log($"[MatoiTest] crouch: height={cc.height:0.00} speed={speed:0.00}");
            Assert.That(speed, Is.InRange(1.2f, motor.Profile.crouchSpeed + 0.05f), "しゃがみ歩きの速さが想定外");

            yield return Tap(pad.dpad.left, 0.4f);
            Assert.IsFalse(motor.IsCrouching, "もう一度押しても立ち上がらない");
            Assert.AreEqual(stand, cc.height, 0.01f, "当たり判定の高さが元に戻っていない");
        }

        [UnityTest]
        public IEnumerator LowCeiling_KeepsPlayerCrouched_UntilClear()
        {
            yield return Load();
            yield return Crouch();
            var roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
            roof.transform.position = motor.transform.position + Vector3.up * 1.55f;
            roof.transform.localScale = new Vector3(3f, 0.2f, 3f);
            Physics.SyncTransforms();
            yield return Tap(pad.dpad.left, 0.4f);
            Assert.IsTrue(motor.IsCrouching, "低い天井の下で立ち上がってしまった");
            Object.Destroy(roof);
            yield return new WaitForSeconds(0.3f);
            Assert.IsFalse(motor.IsCrouching, "天井が無くなっても立ち上がらない");
        }

        [UnityTest]
        public IEnumerator SprintAndJump_BothStandThePlayerUp()
        {
            yield return Load();
            yield return Crouch();
            var l = Quaternion.Euler(0, -motor.transform.eulerAngles.y, 0) * Vector3.forward;
            Set(pad.leftStick, new Vector2(l.x, l.z));
            yield return Tap(pad.leftStickButton, 0.4f);
            Assert.IsFalse(motor.IsCrouching, "走り出しても立ち上がらない");
            Set(pad.leftStick, Vector2.zero);
            yield return new WaitForSeconds(0.5f);

            yield return Crouch();
            float ground = motor.transform.position.y, top = ground;
            Press(pad.buttonSouth);
            yield return null;
            Release(pad.buttonSouth);
            float end = Time.time + 0.6f;
            while (Time.time < end) { top = Mathf.Max(top, motor.transform.position.y); yield return null; }
            Assert.IsFalse(motor.IsCrouching, "ジャンプしても立ち上がらない");
            Assert.Greater(top - ground, 0.6f, "しゃがみからジャンプできない");
        }

        // ---------- ステルス ----------
        [UnityTest]
        public IEnumerator SneakingBehindEnemy_StaysHidden()
        {
            yield return Load();
            yield return Crouch();
            enemy.PlaceAt(motor.transform.position + Vector3.forward * 3f, 0f);   // 敵は北を向き、主人公はその背後
            yield return new WaitForSeconds(3f);
            Debug.Log($"[MatoiTest] behind: state={enemy.Current} awareness={enemy.Awareness:0.00} status={stealth.Status} label={indicator.Label}");
            Assert.AreEqual(EnemyBrain.State.Idle, enemy.Current, "背後でしゃがんでいるのに気づかれた");
            Assert.AreEqual(StealthStatus.Hidden, stealth.Status);
            Assert.AreEqual("［ 隠密 ］", indicator.Label);
        }

        [UnityTest]
        public IEnumerator SneakingInFrontOfEnemy_RaisesAwareness_ThenDetected()
        {
            yield return Load();
            yield return Crouch();
            enemy.PlaceAt(motor.transform.position + Vector3.forward * 5f, 180f);   // 敵はこちらを向いている
            bool caution = false;
            float detectedAt = -1f, start = Time.time;
            while (Time.time < start + 8f && detectedAt < 0f)
            {
                caution |= stealth.Status == StealthStatus.Caution;
                if (stealth.Status == StealthStatus.Detected) detectedAt = Time.time - start;
                yield return null;
            }
            Debug.Log($"[MatoiTest] front: caution={caution} detectedAfter={detectedAt:0.00}s state={enemy.Current} label={indicator.Label}");
            Assert.IsTrue(caution, "見つかる前に［警戒］の段階がない");
            Assert.Greater(detectedAt, 0.5f, "しゃがんでいるのに一瞬で見つかった");
            Assert.AreEqual("［ 発見 ］", indicator.Label);
        }

        [UnityTest]
        public IEnumerator StandingInFrontOfEnemy_IsSpottedAlmostImmediately()
        {
            yield return Load();
            enemy.PlaceAt(motor.transform.position + Vector3.forward * 8f, 180f);
            yield return new WaitForSeconds(0.4f);
            Assert.AreEqual(EnemyBrain.State.Chase, enemy.Current, "立っているのにすぐ見つからない");
        }

        [UnityTest]
        public IEnumerator SneakMeleeFromBehind_DealsTripleDamage()
        {
            yield return Load();
            yield return Crouch();
            enemy.GetComponent<Health>().Configure(500f, Faction.Hostile);
            enemy.PlaceAt(motor.transform.position + Vector3.forward * 1.3f, 0f);
            yield return new WaitForSeconds(0.2f);
            yield return Tap(pad.buttonEast, 0.5f);
            float dealt = 500f - enemy.Health.Current;
            float expected = player.GetComponent<MeleeAttack>().damage * stealth.meleeMultiplier;
            Debug.Log($"[MatoiTest] sneak melee: dealt={dealt:0} expected={expected:0} attacks={stealth.SneakAttackCount}");
            Assert.AreEqual(expected, dealt, 0.01f, "隠密攻撃の近接ダメージが 3 倍になっていない");
            Assert.AreEqual(1, stealth.SneakAttackCount);
            Assert.IsTrue(enemy.IsHuntingPlayer, "攻撃された敵が反撃してこない");
        }

        [UnityTest]
        public IEnumerator SneakGunshot_DoublesDamage_ButAlertsNearbyEnemies()
        {
            yield return Load();
            yield return Crouch();
            var other = EnemyBrain.All.OrderBy(x => x.name).First(e => e != enemy);
            other.PlaceAt(motor.transform.position + new Vector3(-12f, 0f, -2f), 270f);
            var health = enemy.GetComponent<Health>();
            health.Configure(500f, Faction.Hostile);
            enemy.PlaceAt(motor.transform.position + Vector3.forward * 6f, 0f);

            var tp = ThirdPersonCameraController.Instance;
            tp.SetActive(true);
            Press(pad.leftTrigger);
            float aimEnd = Time.time + 0.8f;
            while (Time.time < aimEnd)
            {
                Vector3 d = enemy.transform.position + Vector3.up * 1.2f - Camera.main.transform.position;
                tp.Yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                tp.Pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
                yield return null;
            }
            Press(pad.rightTrigger);
            yield return new WaitUntil(() => player.GetComponent<WeaponShooter>().ShotsFired > 0);
            Release(pad.rightTrigger);
            yield return new WaitForSeconds(0.2f);
            Release(pad.leftTrigger);

            float gun = player.GetComponent<WeaponHolder>().CurrentWeapon.damage;
            float dealt = 500f - health.Current;
            Debug.Log($"[MatoiTest] sneak gun: dealt={dealt:0} expected={gun * stealth.gunMultiplier:0} other={other.Current}");
            Assert.AreEqual(gun * stealth.gunMultiplier, dealt, 0.01f, "隠密攻撃の射撃ダメージが 2 倍になっていない");
            Assert.AreEqual(EnemyBrain.State.Search, other.Current, "銃声を聞いた敵が調べに来ない");
        }

        [UnityTest]
        public IEnumerator HiddenPlayer_EnemyGivesUpAndReturns()
        {
            yield return Load();
            enemy.PlaceAt(new Vector3(-30f, 0.1f, -2f), 90f);
            enemy.ForceAggro();
            yield return new WaitForSeconds(0.2f);
            yield return Crouch();
            // 主人公を高い壁で囲い、敵から見えなくする
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = motor.transform.position + Vector3.left * 2f + Vector3.up * 2f;
            wall.transform.localScale = new Vector3(0.5f, 4f, 12f);
            Physics.SyncTransforms();
            float end = Time.time + 7f;
            while (Time.time < end && enemy.Current == EnemyBrain.State.Chase && Vector3.Distance(enemy.transform.position, motor.transform.position) > 6f)
            {
                enemy.transform.position = enemy.transform.position; // 位置はそのまま
                yield return null;
            }
            float wait = Time.time + 8f;
            while (Time.time < wait && enemy.IsHuntingPlayer) { yield return null; }
            Debug.Log($"[MatoiTest] give up: state={enemy.Current} hunting={enemy.IsHuntingPlayer}");
            Object.Destroy(wall);
            Assert.IsFalse(enemy.IsHuntingPlayer, "隠れ続けても敵があきらめない");
        }

        [UnityTest]
        public IEnumerator PowerArmor_CannotJumpOrCrouch()
        {
            yield return Load();
            var armor = player.GetComponent<Matoi.Equipment.PowerArmorUser>();
            var station = Object.FindAnyObjectByType<Matoi.Equipment.PowerArmorStation>();
            motor.Teleport(station.transform.position + station.transform.forward * 1.2f + Vector3.up * 0.1f, 0f);
            yield return new WaitForSeconds(0.3f);
            yield return Tap(pad.dpad.up, 0.5f);
            yield return new WaitUntil(() => armor.IsArmored);
            yield return new WaitForSeconds(2f);
            float y = motor.transform.position.y;
            yield return Tap(pad.buttonSouth, 0.25f);
            Assert.AreEqual(y, motor.transform.position.y, 0.05f, "パワーアーマーでジャンプできてしまう");
            yield return Tap(pad.dpad.left, 0.3f);
            Assert.IsFalse(motor.IsCrouching, "パワーアーマーでしゃがめてしまう");
        }

        void Shot(string name)
        {
            var go = new GameObject("TempShot");
            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(Camera.main);
            cam.fieldOfView = 36f;
            Vector3 p = motor.transform.position + Vector3.up * 0.7f;
            go.transform.position = p + Quaternion.Euler(0, 60, 0) * motor.transform.forward * 3f + Vector3.up * 0.3f;
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
