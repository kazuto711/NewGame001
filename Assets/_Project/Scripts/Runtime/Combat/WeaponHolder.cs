using System;
using UnityEngine;

namespace Matoi.Combat
{
    /// <summary>
    /// 武器を「持つ・しまう・構える・持ち替える」を担当する。
    ///  - しまっている時：拳銃は右太もも、ライフルは背中
    ///  - 持っている時：上半身を武器の構えアニメーションにする
    ///      拳銃   … 手首に付ける
    ///      ライフル … 右肩に銃床を当てる位置に置き、両腕を IK で銃に合わせる
    ///  - 構えている時：上半身をひねって狙う方向へ向け、銃を狙う方向へそろえる（脚は移動方向のまま）
    ///  - 撃った時：反動で銃が後ろ・上に跳ね、ばねのように戻る
    /// 骨の向き（軸）に依存しない計算にしているので、モデルを差し替えても動く。
    /// </summary>
    [DefaultExecutionOrder(200)] // Animator・体の傾き（CharacterAnimator）の後に実行する
    public class WeaponHolder : MonoBehaviour
    {
        static readonly int DrawnId = Animator.StringToHash("WeaponDrawn");
        static readonly int AimingId = Animator.StringToHash("Aiming");
        static readonly int RifleStanceId = Animator.StringToHash("RifleStance");

        [SerializeField] Animator animator;
        [Tooltip("持ち物（順番に持ち替える）")]
        [SerializeField] WeaponDefinition[] loadout;
        [SerializeField] LineRenderer aimLine;
        [Tooltip("上半身レイヤーの名前（Animator Controller 内）")]
        [SerializeField] string upperBodyLayer = "UpperBody";
        [SerializeField] float upperBodyBlendSpeed = 7f;

        [Header("上半身のひねり")]
        [Tooltip("脚の向きに対して上半身をひねれる最大角度")]
        [SerializeField] float maxTwist = 110f;
        [Tooltip("ひねりを 腰 / 胸 / 胸上部 にどの割合で分けるか")]
        [SerializeField] Vector3 twistWeights = new Vector3(0.25f, 0.35f, 0.4f);
        [SerializeField] float twistSharpness = 18f;

        [Header("反動の戻り")]
        [SerializeField] float recoilReturn = 14f;
        [Tooltip("撃った時に上半身がのけぞる量（銃口の跳ね上がりの角度に掛ける）")]
        [SerializeField] float torsoRecoil = 0.3f;

        [Header("両手の IK を解除する時間（拳銃：攻撃・被弾・リロードの間はアニメーションに腕を任せる）")]
        [SerializeField] float ikReleaseTime = 0.12f;
        [Tooltip("回避などで、ライフルを背中に回すのにかける時間（秒）")]
        [SerializeField] float slingTime = 0.2f;
        float ikRelease, slingBlend;
        Matoi.Characters.CharacterAnimator characterAnimator;
        WeaponShooter shooter;
        MeleeAttack melee;
        Matoi.Equipment.PowerArmorUser armorUser;
        Transform hips, head, rIndex, rLittle, rMiddle, lIndex, lLittle, lMiddle;
        Transform[][] rFingers, lFingers;   // 人差し指・中指・薬指・小指の 3 関節

        // ライフルを握る指の曲げ（度）：付け根・第二関節・指先
        static readonly Vector3[] RifleRightCurl =
        {
            new Vector3(18f, 30f, 15f),   // 人差し指は引き金の横に伸ばす
            new Vector3(62f, 78f, 42f), new Vector3(66f, 80f, 42f), new Vector3(70f, 80f, 40f),
        };
        static readonly Vector3[] RifleLeftCurl =
        {
            new Vector3(38f, 42f, 28f), new Vector3(42f, 45f, 28f), new Vector3(46f, 48f, 28f), new Vector3(50f, 50f, 28f),
        };

        // 弾倉：リロードで外す部品の、銃に差さっている時の位置
        bool magCaptured, magDropped;
        Vector3 magRestPos;
        Quaternion magRestRot = Quaternion.identity;

        /// <summary>1 ＝ 腕をアニメーションに任せている（拳銃）。</summary>
        public float IkRelease => ikRelease;
        /// <summary>1 ＝ ライフルを背中に回している（回避・死亡）。</summary>
        public float SlingBlend => slingBlend;
        /// <summary>弾倉が銃に差さっているか（リロードの途中は false）。</summary>
        public bool MagazineInWell { get; private set; } = true;

        [Header("しまった時の位置（モデルの体格に合わせる）")]
        [Tooltip("胸の骨から背中側へ離す距離（リュックがあるモデルは大きくする）")]
        [SerializeField] float backHolsterDistance = 0.2f;
        [Tooltip("太ももの骨から外側へ離す距離")]
        [SerializeField] float thighHolsterOffset = 0.12f;

        Weapon[] instances;
        int index = -1;
        bool drawn, aiming;
        Vector3 aimDir = Vector3.forward;
        float upperWeight, twist, aimBlend;
        float recoilBack, recoilPitch;
        int layerIndex = -1;
        Transform hand, lowerArm, upperArm, lHand, lLowerArm, lUpperArm;
        Transform thigh, calf, spine, chest, upperChest;

        public WeaponDefinition CurrentWeapon => index >= 0 ? loadout[index] : null;
        public Weapon WeaponInstance => index >= 0 ? instances[index] : null;
        public int CurrentIndex => index;
        public int WeaponCount => loadout != null ? loadout.Length : 0;
        public bool IsDrawn => drawn;
        public bool IsAiming => aiming && drawn;
        public Vector3 AimDirection => aimDir;
        public float UpperBodyWeight => upperWeight;
        public float Twist => twist;
        /// <summary>左手を銃に添える強さ（リロード中は 0 にして手を離す）</summary>
        public float SupportHandWeight { get; set; } = 1f;

        // ---- 三人称：画面中央の先にある「照準点」（null なら水平に狙う＝見下ろし視点） ----
        Vector3? aimPoint;
        float elevation;   // 狙いの上下の角度（度、上が＋）。見た目用になめらかにした値
        [Header("上下の狙い")]
        [Tooltip("上半身を上下に傾けられる最大角度")]
        [SerializeField] float maxElevation = 45f;

        public Vector3? AimPoint => aimPoint;
        public float Elevation => elevation;
        /// <summary>照準の線（レーザー）を出すか。三人称では照準マークを使うので消す</summary>
        public bool ShowAimLine { get; set; } = true;

        /// <summary>照準点を設定する（三人称）。null で水平の狙いに戻す（見下ろし）。</summary>
        public void SetAimPoint(Vector3? point) => aimPoint = point;

        /// <summary>from（銃口など）から撃つ向き。照準点があればそこへ、無ければ水平の狙いの向き。</summary>
        public Vector3 GetFireDirection(Vector3 from)
        {
            if (aimPoint.HasValue && IsAiming)
            {
                Vector3 d = aimPoint.Value - from;
                if (d.sqrMagnitude > 0.01f) return d.normalized;
            }
            return aimDir;
        }

        float TargetElevation()
        {
            if (!aimPoint.HasValue || !IsAiming) return 0f;
            Vector3 from = upperChest ? upperChest.position : transform.position + Vector3.up * 1.4f;
            Vector3 d = aimPoint.Value - from;
            float flat = new Vector2(d.x, d.z).magnitude;
            return Mathf.Clamp(Mathf.Atan2(d.y, Mathf.Max(0.3f, flat)) * Mathf.Rad2Deg, -maxElevation, maxElevation);
        }

        /// <summary>水平の狙い方向を、上下の角度ぶん傾けた向き（見た目用）。</summary>
        Vector3 Aim3D()
        {
            Vector3 right = Vector3.Cross(Vector3.up, aimDir);
            if (right.sqrMagnitude < 1e-4f) return aimDir;
            return Quaternion.AngleAxis(-elevation, right.normalized) * aimDir;
        }

        public event Action<WeaponDefinition> WeaponChanged;

        void Awake()
        {
            if (!animator) animator = GetComponentInChildren<Animator>();
            characterAnimator = GetComponent<Matoi.Characters.CharacterAnimator>();
            shooter = GetComponent<WeaponShooter>();
            melee = GetComponent<MeleeAttack>();
            armorUser = GetComponent<Matoi.Equipment.PowerArmorUser>();
            hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            head = animator.GetBoneTransform(HumanBodyBones.Head);
            rFingers = FingerBones(HumanBodyBones.RightIndexProximal);
            lFingers = FingerBones(HumanBodyBones.LeftIndexProximal);
            rIndex = animator.GetBoneTransform(HumanBodyBones.RightIndexProximal);
            rLittle = animator.GetBoneTransform(HumanBodyBones.RightLittleProximal);
            rMiddle = animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            lIndex = animator.GetBoneTransform(HumanBodyBones.LeftIndexProximal);
            lLittle = animator.GetBoneTransform(HumanBodyBones.LeftLittleProximal);
            lMiddle = animator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
            hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            lowerArm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            upperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            lHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            lLowerArm = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            lUpperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            thigh = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            calf = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            upperChest = animator.GetBoneTransform(HumanBodyBones.UpperChest);
            layerIndex = animator.GetLayerIndex(upperBodyLayer);
            if (aimLine) aimLine.enabled = false;

            int n = loadout != null ? loadout.Length : 0;
            instances = new Weapon[n];
            for (int i = 0; i < n; i++)
            {
                var def = loadout[i];
                if (!def || !def.prefab) continue;
                instances[i] = Instantiate(def.prefab, transform);
                instances[i].transform.localScale = Vector3.one * def.visualScale;
            }
            if (n > 0) Select(0);
        }

        /// <summary>人差し指から小指までの骨（HumanBodyBones は指ごとに 3 つずつ連番）。</summary>
        Transform[][] FingerBones(HumanBodyBones indexProximal)
        {
            var fingers = new Transform[4][];
            for (int f = 0; f < 4; f++)
            {
                fingers[f] = new Transform[3];
                for (int j = 0; j < 3; j++)
                    fingers[f][j] = animator.GetBoneTransform((HumanBodyBones)((int)indexProximal + f * 3 + j));
            }
            return fingers;
        }

        /// <summary>主人公のモデルに合わせて、しまった武器の位置を変える。</summary>
        public void SetHolsterDistances(float back, float thigh)
        {
            backHolsterDistance = back;
            thighHolsterOffset = thigh;
        }

        /// <summary>持ち物の i 番目に持ち替える。</summary>
        public void Select(int i)
        {
            if (loadout == null || i < 0 || i >= loadout.Length || i == index) return;
            index = i;
            recoilBack = recoilPitch = 0f;
            WeaponChanged?.Invoke(CurrentWeapon);
        }

        public void SelectNext() { if (WeaponCount > 0) Select((index + 1) % WeaponCount); }

        public void SetDrawn(bool value) { if (CurrentWeapon) drawn = value; if (!drawn) aiming = false; }
        public void ToggleDrawn() => SetDrawn(!drawn);

        /// <summary>構えの状態と狙う方向（ワールド・水平）を設定する。毎フレーム呼ぶ。</summary>
        public void SetAim(bool value, Vector3 direction)
        {
            aiming = value && drawn;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.0001f) aimDir = direction.normalized;
        }

        /// <summary>撃った時の反動を加える（WeaponShooter から呼ばれる）。</summary>
        public void AddRecoil(float back, float pitchDeg)
        {
            recoilBack = Mathf.Min(recoilBack + back, 0.15f);
            recoilPitch = Mathf.Min(recoilPitch + pitchDeg, 25f);
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            animator.SetBool(DrawnId, drawn);
            animator.SetBool(AimingId, IsAiming);
            animator.SetBool(RifleStanceId, drawn && CurrentWeapon && CurrentWeapon.stance == WeaponStance.Rifle);
            bool armsBusy = (characterAnimator && characterAnimator.ActionOwnsArms) || (shooter && shooter.IsReloading);
            ikRelease = Mathf.MoveTowards(ikRelease, armsBusy ? 1f : 0f, dt / Mathf.Max(0.01f, ikReleaseTime));
            bool tumbling = characterAnimator && characterAnimator.TumblingBody;
            slingBlend = Mathf.MoveTowards(slingBlend, tumbling ? 1f : 0f, dt / Mathf.Max(0.01f, slingTime));
            upperWeight =Mathf.MoveTowards(upperWeight, drawn ? 1f : 0f, upperBodyBlendSpeed * dt);
            if (layerIndex >= 0) animator.SetLayerWeight(layerIndex, upperWeight);
            aimBlend = Mathf.MoveTowards(aimBlend, IsAiming ? 1f : 0f, 8f * dt);

            float k = 1f - Mathf.Exp(-recoilReturn * dt);
            recoilBack = Mathf.Lerp(recoilBack, 0f, k);
            recoilPitch = Mathf.Lerp(recoilPitch, 0f, k);

            ApplyTwist(dt);
            for (int i = 0; i < instances.Length; i++)
                if (instances[i]) PlaceWeapon(i, i == index);
            UpdateAimLine();
        }

        void ApplyTwist(float dt)
        {
            Vector3 fwd = transform.forward; fwd.y = 0f;
            float target = IsAiming ? Mathf.Clamp(Vector3.SignedAngle(fwd, aimDir, Vector3.up), -maxTwist, maxTwist) : 0f;
            twist = Mathf.Lerp(twist, target, 1f - Mathf.Exp(-twistSharpness * dt));
            elevation = Mathf.Lerp(elevation, TargetElevation(), 1f - Mathf.Exp(-twistSharpness * dt));
            if (Mathf.Abs(twist) > 0.01f)
            {
                Rotate(spine, twist * twistWeights.x, Vector3.up);
                Rotate(chest, twist * twistWeights.y, Vector3.up);
                Rotate(upperChest ? upperChest : chest, twist * twistWeights.z, Vector3.up);
            }
            if (Mathf.Abs(elevation) > 0.01f)
            {
                // 上下の狙い：ひねった後の上半身の「右」を軸に、反らす／かがむ
                Vector3 right = Vector3.Cross(Vector3.up, aimDir).normalized;
                Rotate(spine, -elevation * twistWeights.x, right);
                Rotate(chest, -elevation * twistWeights.y, right);
                Rotate(upperChest ? upperChest : chest, -elevation * twistWeights.z, right);
            }
            if (recoilPitch > 0.01f)
            {
                // 撃った反動で、胸から上が少しのけぞる
                Vector3 right = Vector3.Cross(Vector3.up, aimDir).normalized;
                Rotate(upperChest ? upperChest : chest, -recoilPitch * torsoRecoil, right);
            }
        }

        static void Rotate(Transform bone, float angle, Vector3 axis)
        {
            if (bone) bone.rotation = Quaternion.AngleAxis(angle, axis) * bone.rotation;
        }

        void PlaceWeapon(int i, bool selected)
        {
            var def = loadout[i];
            Transform w = instances[i].transform;
            if (!(selected && drawn)) RestoreMagazine(instances[i]);
            // パワーアーマーに乗っている間は、しまった武器を隠す（装甲からはみ出さないように）
            bool show = (selected && drawn) || !(armorUser && armorUser.IsArmored);
            if (instances[i].gameObject.activeSelf != show) instances[i].gameObject.SetActive(show);
            if (selected && drawn)
            {
                if (def.stance == WeaponStance.Rifle) PlaceRifle(def, instances[i]);
                else PlacePistol(def, w);
            }
            else if (def.holster == HolsterSlot.Back && (upperChest || chest))
            {
                GetBackSlingPose(out Vector3 slingPos, out Quaternion slingRot);
                w.SetPositionAndRotation(slingPos, slingRot);
            }
            else if (thigh && calf)
            {
                // 右太ももの外側に沿わせる（銃口は下、脚の振りに追従する）
                Vector3 along = (calf.position - thigh.position).normalized;
                Vector3 outward = Vector3.ProjectOnPlane(transform.right, along).normalized;
                Vector3 front = Vector3.Cross(along, outward); // 銃の上面を前へ＝グリップが後ろ（右手で抜ける向き）
                // 下ろした右手と重ならないよう、太ももの中ほど・少し後ろに付ける
                Vector3 p = Vector3.Lerp(thigh.position, calf.position, 0.52f) + outward * thighHolsterOffset - front * 0.035f;
                w.SetPositionAndRotation(p, Quaternion.LookRotation(along, front));
            }
        }

        /// <summary>背中に斜めにかけた時の位置と向き（銃口は右下、頭に当たらない向き）。胸の動きに追従する。</summary>
        void GetBackSlingPose(out Vector3 position, out Quaternion rotation)
        {
            Transform c = upperChest ? upperChest : chest;
            Vector3 right = upperArm && lUpperArm ? (upperArm.position - lUpperArm.position).normalized : transform.right;
            Vector3 up = spine ? (c.position - spine.position).normalized : transform.up;
            Vector3 forward = Vector3.Cross(right, up).normalized;
            up = Vector3.Cross(forward, right);
            Vector3 dir = (right * 0.6f - up * 0.8f).normalized;
            // 銃の側面を背中に沿わせる（弾倉が体やリュックに刺さらないように）
            position = c.position - forward * backHolsterDistance - up * 0.1f;
            rotation = Quaternion.LookRotation(dir, Vector3.Cross(forward, dir));
        }

        Quaternion WithRecoil(Vector3 dir, Vector3 up)
        {
            Vector3 right = Vector3.Cross(up, dir).normalized;
            return Quaternion.AngleAxis(-recoilPitch, right) * Quaternion.LookRotation(dir, up);
        }

        /// <summary>手の向き（指の付け根へ向かう方向と、手のひらの向き）を、骨の軸に依存せず求める。</summary>
        static bool TryHandFrame(Transform wrist, Transform index, Transform little, Transform middle, bool rightHand, out Quaternion frame)
        {
            frame = Quaternion.identity;
            if (!wrist || !index || !little || !middle) return false;
            Vector3 dir = middle.position - wrist.position;
            Vector3 palm = Vector3.Cross(index.position - little.position, dir) * (rightHand ? -1f : 1f);
            if (dir.sqrMagnitude < 1e-6f || palm.sqrMagnitude < 1e-8f) return false;
            frame = Quaternion.LookRotation(dir, palm);
            return true;
        }

        /// <summary>手を、指定の向きへ回す。</summary>
        static void AlignHand(Transform wrist, Transform index, Transform little, Transform middle, bool rightHand, Quaternion target, float weight)
        {
            if (weight <= 0f || !TryHandFrame(wrist, index, little, middle, rightHand, out Quaternion current)) return;
            Quaternion aligned = target * Quaternion.Inverse(current) * wrist.rotation;
            wrist.rotation = Quaternion.Slerp(wrist.rotation, aligned, weight);
        }

        /// <summary>指を手のひらの側へ曲げて握らせる。curl は指ごとの（付け根, 第二関節, 指先）の角度。</summary>
        static void CurlFingers(Transform wrist, Transform index, Transform little, Transform middle, bool rightHand,
                                Transform[][] fingers, Vector3[] curl, float weight)
        {
            if (weight <= 0f || fingers == null || !TryHandFrame(wrist, index, little, middle, rightHand, out Quaternion frame)) return;
            Vector3 dir = frame * Vector3.forward;
            Vector3 axis = Vector3.Cross(dir, frame * Vector3.up).normalized;
            for (int f = 0; f < fingers.Length; f++)
            {
                Transform[] bones = fingers[f];
                if (!bones[0] || !bones[1] || !bones[2]) continue;
                float angle = 0f;
                for (int j = 0; j < 2; j++)
                {
                    angle += curl[f][j];
                    Vector3 target = Quaternion.AngleAxis(angle, axis) * dir;
                    Quaternion aligned = Quaternion.FromToRotation(bones[j + 1].position - bones[j].position, target) * bones[j].rotation;
                    bones[j].rotation = Quaternion.Slerp(bones[j].rotation, aligned, weight);
                }
                bones[2].rotation = Quaternion.AngleAxis(curl[f][2] * weight, axis) * bones[2].rotation;
            }
        }

        void PlacePistol(WeaponDefinition def, Transform w)
        {
            if (!hand || !lowerArm) return;
            Quaternion gripFrame = Quaternion.LookRotation(def.pistolHandDir, def.pistolPalmDir); // 銃から見た右手の向き

            // 構えていない時：銃は右手の中にある
            Vector3 handPos = hand.position;
            Quaternion handRot = w.rotation;
            if (TryHandFrame(hand, rIndex, rLittle, rMiddle, true, out Quaternion frame))
                handRot = frame * Quaternion.Inverse(gripFrame);
            handPos -= handRot * def.pistolWristLocal;

            // 構えている時：銃を狙いへ向けて体の前に置き、両手を銃に合わせる
            float aim = aimBlend * upperWeight * (1f - ikRelease);
            if (aim <= 0f) { w.SetPositionAndRotation(handPos, handRot); return; }

            Vector3 dir = Aim3D();
            Vector3 right = Vector3.Cross(Vector3.up, dir);
            if (right.sqrMagnitude < 1e-4f) right = transform.right;
            right.Normalize();
            Vector3 up = Vector3.Cross(dir, right);
            Vector3 aimPos = upperArm.position - right * 0.13f - up * 0.04f + dir * (0.46f - recoilBack);
            w.SetPositionAndRotation(Vector3.Lerp(handPos, aimPos, aim), Quaternion.Slerp(handRot, WithRecoil(dir, up), aim));

            Vector3 rHint = upperArm.position + right * 0.35f - up * 0.4f;
            TwoBoneIK.Solve(upperArm, lowerArm, hand, w.position + w.rotation * def.pistolWristLocal, rHint, aim);
            AlignHand(hand, rIndex, rLittle, rMiddle, true, w.rotation * gripFrame, aim);

            Vector3 lHint = lUpperArm.position - right * 0.35f - up * 0.4f;
            float support = aim * SupportHandWeight;
            TwoBoneIK.Solve(lUpperArm, lLowerArm, lHand, w.position + w.rotation * def.pistolSupportWrist, lHint, support);
            AlignHand(lHand, lIndex, lLittle, lMiddle, false,
                w.rotation * Quaternion.LookRotation(def.pistolSupportDir, def.pistolSupportPalm), support);
        }

        static float Smooth(float t) => Mathf.SmoothStep(0f, 1f, t);
        /// <summary>a→b で 0→1 に上がり、c→d で 1→0 に下がる値。</summary>
        static float Window(float t, float a, float b, float c, float d) =>
            Smooth(Mathf.InverseLerp(a, b, t)) * (1f - Smooth(Mathf.InverseLerp(c, d, t)));

        /// <summary>弾倉を銃の元の位置に戻す。</summary>
        void RestoreMagazine(Weapon weapon)
        {
            var mag = weapon ? weapon.Magazine : null;
            if (!mag) return;
            if (!magCaptured)
            {
                magRestPos = mag.localPosition;
                magRestRot = mag.localRotation;
                magCaptured = true;
            }
            mag.localPosition = magRestPos;
            mag.localRotation = magRestRot;
            var r = mag.GetComponent<Renderer>();
            if (r) r.enabled = true;
            magDropped = false;
            MagazineInWell = true;
        }

        /// <summary>空になった弾倉を地面へ落とす。</summary>
        void DropMagazine(Transform mag)
        {
            var dropped = Instantiate(mag.gameObject, mag.position, mag.rotation);
            dropped.name = "DroppedMagazine";
            dropped.transform.localScale = mag.lossyScale;
            dropped.layer = 2;   // 弾や照準の判定に当たらないようにする
            dropped.AddComponent<BoxCollider>();
            var body = dropped.AddComponent<Rigidbody>();
            body.mass = 0.4f;
            body.linearVelocity = -transform.right * 0.4f + transform.forward * 0.1f;
            Destroy(dropped, 6f);
        }

        /// <summary>
        /// ライフルのリロードで左手を動かす：弾倉をつかむ→抜いて落とす→腰の予備を取る→差し込む→元の位置へ。
        /// 右手は銃を握ったまま。戻り値は左手の目標位置。
        /// </summary>
        Vector3 RifleReloadLeftHand(Weapon weapon, float t, Vector3 support)
        {
            Transform w = weapon.transform, mag = weapon.Magazine;
            Vector3 well = mag.parent.TransformPoint(magRestPos);
            Vector3 grab = well - w.up * 0.20f - w.forward * 0.02f;
            Vector3 drop = grab - Vector3.up * 0.18f - w.right * 0.06f;
            Vector3 pouch = (hips ? hips.position : transform.position + Vector3.up) - transform.right * 0.24f + transform.forward * 0.04f + Vector3.up * 0.02f;
            Vector3 approach = grab - w.up * 0.10f;
            if (t < 0.13f) return Vector3.Lerp(support, grab, Smooth(t / 0.13f));
            if (t < 0.25f) return Vector3.Lerp(grab, drop, Smooth(Mathf.InverseLerp(0.13f, 0.25f, t)));
            if (t < 0.45f) return Vector3.Lerp(drop, pouch, Smooth(Mathf.InverseLerp(0.25f, 0.45f, t)));
            if (t < 0.52f) return pouch;
            if (t < 0.70f) return Vector3.Lerp(pouch, approach, Smooth(Mathf.InverseLerp(0.52f, 0.70f, t)));
            if (t < 0.78f) return Vector3.Lerp(approach, grab, Smooth(Mathf.InverseLerp(0.70f, 0.78f, t)));
            if (t < 0.90f) return Vector3.Lerp(grab, support, Smooth(Mathf.InverseLerp(0.78f, 0.90f, t)));
            return support;
        }

        /// <summary>リロードの進み具合に合わせて、弾倉を左手に持たせる・隠す・銃に戻す。</summary>
        void UpdateMagazineDuringReload(Weapon weapon, float t)
        {
            Transform w = weapon.transform, mag = weapon.Magazine;
            Vector3 restPos = mag.parent.TransformPoint(magRestPos);
            Quaternion restRot = mag.parent.rotation * magRestRot;
            Vector3 carried = lHand.position + w.forward * 0.06f + w.up * 0.16f;

            if (t >= 0.25f && !magDropped)
            {
                mag.SetPositionAndRotation(carried, restRot);
                DropMagazine(mag);
                magDropped = true;
            }
            var r = mag.GetComponent<Renderer>();
            if (r) r.enabled = t < 0.25f || t >= 0.45f;   // 落としてから予備を取るまでは、銃に弾倉が無い
            MagazineInWell = t < 0.13f || t >= 0.78f;

            if (t < 0.13f || t >= 0.78f) mag.SetPositionAndRotation(restPos, restRot);
            else if (t >= 0.70f) mag.SetPositionAndRotation(Vector3.Lerp(carried, restPos, Smooth(Mathf.InverseLerp(0.70f, 0.78f, t))), restRot);
            else mag.SetPositionAndRotation(carried, restRot);
        }

        void PlaceRifle(WeaponDefinition def, Weapon weapon)
        {
            if (!upperArm) return;
            Transform w = weapon.transform;
            // 狙う向き：構え中は狙いの方向、下げ構えでは体の正面から銃口を下げる
            Vector3 fwd = Vector3.Slerp(FlatForwardOfChest(), aimDir, aimBlend);
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
            // 下げ構え（銃口を下げる）と、狙いの上下（上が＋）をまとめて傾ける
            Vector3 dir = Quaternion.AngleAxis(def.lowReadyPitch * (1f - aimBlend) - elevation * aimBlend, right) * fwd;
            Vector3 up = Vector3.Cross(dir, right);

            Vector3 s = Vector3.Lerp(def.shoulderOffset, def.aimShoulderOffset, aimBlend);
            Vector3 pos = upperArm.position + right * s.x + Vector3.up * s.y + fwd * s.z - dir * recoilBack;
            Quaternion rot = WithRecoil(dir, up);

            // リロード中：弾倉が左手に近づくよう、銃を手前に引いて傾ける
            bool reloading = shooter && shooter.IsReloading && weapon.Magazine;
            float reloadT = reloading ? shooter.ReloadProgress : 0f;
            if (reloading)
            {
                float k = Window(reloadT, 0f, 0.1f, 0.88f, 1f);
                pos += (-dir * 0.06f - up * 0.05f) * k;
                rot = Quaternion.AngleAxis(-28f * k, dir) * Quaternion.AngleAxis(-18f * k, right) * rot;
            }

            // 近接攻撃：銃を体の前に横たえ、両手で前へ突き出す
            float meleeT = melee ? melee.Progress : -1f;
            if (meleeT >= 0f)
            {
                float hit = melee.HitProgress;
                float thrust = meleeT < hit ? Smooth(meleeT / hit) : 1f - Smooth(Mathf.InverseLerp(hit, 1f, meleeT));
                float blend = Window(meleeT, 0f, 0.16f, 0.72f, 1f);
                // 両腕が届き、頭にも当たらない範囲で前へ押し出す
                Vector3 shovePos = upperArm.position - right * 0.04f - Vector3.up * 0.19f + fwd * (0.14f + 0.18f * thrust);
                Vector3 shoveDir = (-right * 0.95f + Vector3.up * 0.2f + fwd * 0.15f).normalized;
                pos = Vector3.Lerp(pos, shovePos, blend);
                rot = Quaternion.Slerp(rot, Quaternion.LookRotation(shoveDir, Vector3.up), blend);
            }

            // 回避・死亡：体ごと転がるので、長い銃は背中に回す
            if (slingBlend > 0f)
            {
                GetBackSlingPose(out Vector3 slingPos, out Quaternion slingRot);
                float slingT = Smooth(slingBlend);
                pos = Vector3.Lerp(pos, slingPos, slingT);
                rot = Quaternion.Slerp(rot, slingRot, slingT);
            }
            w.SetPositionAndRotation(pos, rot);

            // 両腕を IK で銃に合わせる（肘は外・下向きに）
            float ikWeight = upperWeight * (1f - slingBlend);
            float scale = def.visualScale;
            Vector3 rTarget = w.TransformPoint(def.rightWristOffset / scale);
            Vector3 rHint = upperArm.position + right * 0.3f - Vector3.up * 0.45f - fwd * 0.05f; // 右肘は斜め下へ
            TwoBoneIK.Solve(upperArm, lowerArm, hand, rTarget, rHint, ikWeight);
            AlignHand(hand, rIndex, rLittle, rMiddle, true, w.rotation * Quaternion.LookRotation(def.rifleRightHandDir, def.rifleRightPalm), ikWeight);
            CurlFingers(hand, rIndex, rLittle, rMiddle, true, rFingers, RifleRightCurl, ikWeight);

            // 狙う時は頬を銃床へ寄せる（首を右へ傾け、少しうつむく）
            if (head && aimBlend > 0f)
            {
                Rotate(head, -9f * aimBlend * ikWeight, fwd);
                Rotate(head, 7f * aimBlend * ikWeight, right);
            }

            if (weapon.SupportHand)
            {
                Vector3 lTarget = weapon.SupportHand.position + w.rotation * def.leftWristOffset;
                if (!reloading) RestoreMagazine(weapon);
                else lTarget = RifleReloadLeftHand(weapon, reloadT, lTarget);
                Vector3 lHint = lUpperArm.position - right * 0.12f - Vector3.up * 0.5f + fwd * 0.2f;   // 左肘は銃の下へ
                TwoBoneIK.Solve(lUpperArm, lLowerArm, lHand, lTarget, lHint, ikWeight);
                // リロードで手を離している間は、握りを解く
                float leftGrip = ikWeight * (reloading ? 1f - Window(reloadT, 0.01f, 0.06f, 0.88f, 0.97f) : 1f);
                AlignHand(lHand, lIndex, lLittle, lMiddle, false, w.rotation * Quaternion.LookRotation(def.rifleLeftHandDir, def.rifleLeftPalm), leftGrip);
                CurlFingers(lHand, lIndex, lLittle, lMiddle, false, lFingers, RifleLeftCurl, leftGrip);
                if (reloading)
                {
                    // 弾倉を持つ間は、手のひらを上に向けて下から支える
                    float palmUp = Mathf.Max(Window(reloadT, 0.06f, 0.13f, 0.22f, 0.27f), Window(reloadT, 0.45f, 0.52f, 0.76f, 0.84f));
                    AlignHand(lHand, lIndex, lLittle, lMiddle, false, Quaternion.LookRotation(w.forward, w.up), palmUp * ikWeight);
                    UpdateMagazineDuringReload(weapon, reloadT);
                }
            }
        }

        Vector3 FlatForwardOfChest()
        {
            Vector3 f = Quaternion.AngleAxis(twist, Vector3.up) * transform.forward;
            f.y = 0f;
            return f.normalized;
        }

        void UpdateAimLine()
        {
            if (!aimLine) return;
            var w = WeaponInstance;
            bool show = ShowAimLine && IsAiming && w && upperWeight > 0.9f;
            aimLine.enabled = show;
            if (!show) return;
            Vector3 start = w.Muzzle.position;
            aimLine.positionCount = 2;
            aimLine.SetPosition(0, start);
            aimLine.SetPosition(1, start + GetFireDirection(start) * CurrentWeapon.aimLineLength);
        }
    }
}
