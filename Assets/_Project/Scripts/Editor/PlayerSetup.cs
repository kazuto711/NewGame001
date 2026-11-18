// M2: 移動プロファイル・プレイヤープレハブ・追従カメラを作り、TestField シーンに配置するエディタツール。
// メニュー「Matoi > Install Player (M2)」。
using System.Linq;
using Matoi.CameraSystem;
using Matoi.Characters;
using Matoi.Combat;
using Matoi.DebugTools;
using Matoi.InputHandling;
using Matoi.Player;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

namespace Matoi.EditorTools
{
    public static class PlayerSetup
    {
        const string DataDir = "Assets/_Project/Data/Movement";
        const string PrefabPath = "Assets/_Project/Prefabs/Player.prefab";
        const string InputPath = "Assets/_Project/Settings/Input/MatoiControls.inputactions";
        public static readonly Vector3 SpawnPoint = new Vector3(1.5f, 0.05f, -16f);
        public const float CameraPitch = 50f, CameraYaw = 20f, CameraDistance = 19f;

        [MenuItem("Matoi/Install Player (M2 プレイヤー配置)")]
        public static void InstallIntoTestField()
        {
            var scene = EditorSceneManager.OpenScene(TestFieldBuilder.ScenePath, OpenSceneMode.Single);
            InstallIntoOpenScene();
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Matoi] Player installed.");
        }

        /// <summary>現在開いているシーンにプレイヤーとカメラを配置する。</summary>
        public static void InstallIntoOpenScene()
        {
            var human = CreateHumanProfile();
            var armor = CreatePowerArmorProfile();
            var prefab = CreatePlayerPrefab(human, armor);

            foreach (var name in new[] { "PlayerPlaceholder", "Player", "CM PlayerCamera" })
            {
                var old = GameObject.Find(name);
                if (old) Object.DestroyImmediate(old);
            }

            var player = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            player.transform.SetPositionAndRotation(SpawnPoint, Quaternion.Euler(0, CameraYaw, 0));

            SetupCamera(player.transform);
            InstallCombatFx();
            InstallDamageVolume(player);
        }

        /// <summary>被弾時に画面のふちを赤くするポストプロセス（普段は重み 0）。</summary>
        static void InstallDamageVolume(GameObject player)
        {
            var old = GameObject.Find("DamageVolume");
            if (old) Object.DestroyImmediate(old);
            const string path = "Assets/_Project/Settings/Environment/Damage_Volume.asset";
            AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
            var vig = profile.Add<UnityEngine.Rendering.Universal.Vignette>(true);
            vig.name = "Vignette";
            AssetDatabase.AddObjectToAsset(vig, profile);
            vig.color.Override(new Color(0.75f, 0.03f, 0.02f));
            vig.intensity.Override(0.5f);
            vig.smoothness.Override(0.65f);
            EditorUtility.SetDirty(profile);

            var go = new GameObject("DamageVolume");
            var vol = go.AddComponent<UnityEngine.Rendering.Volume>();
            vol.isGlobal = true;
            vol.priority = 10f;
            vol.weight = 0f;
            vol.sharedProfile = profile;
            SetField(player.GetComponent<Matoi.Player.PlayerLife>(), "damageVolume", vol);
        }

        // ---------- 移動プロファイル ----------
        static MovementProfile LoadOrCreate(string name)
        {
            EnsureFolder(DataDir);
            string path = $"{DataDir}/{name}.asset";
            var p = AssetDatabase.LoadAssetAtPath<MovementProfile>(path);
            if (p == null)
            {
                p = ScriptableObject.CreateInstance<MovementProfile>();
                AssetDatabase.CreateAsset(p, path);
            }
            return p;
        }

        static MovementProfile CreateHumanProfile()
        {
            // 既存アセットがあれば値は上書きしない（手で調整した値を守る）
            bool exists = AssetDatabase.LoadAssetAtPath<MovementProfile>($"{DataDir}/Move_Human.asset") != null;
            var p = LoadOrCreate("Move_Human");
            if (!exists) EditorUtility.SetDirty(p); // 既定値（MovementProfile の初期値）をそのまま使う
            return p;
        }

        static MovementProfile CreatePowerArmorProfile()
        {
            bool exists = AssetDatabase.LoadAssetAtPath<MovementProfile>($"{DataDir}/Move_PowerArmor.asset") != null;
            var p = LoadOrCreate("Move_PowerArmor");
            if (!exists)
            {
                // 重い・止まりにくい・曲がりにくい
                p.walkSpeed = 1.3f;
                p.jogSpeed = 3.0f;
                p.sprintSpeed = 4.6f;
                p.acceleration = 5.5f;
                p.sprintAcceleration = 3.5f;
                p.deceleration = 7f;
                p.turnSpeedIdle = 240f;
                p.turnSpeedMoving = 170f;
                p.turnSpeedSprint = 90f;
                p.sharpTurnSpeedFactor = 0.25f;
                p.pivotAngle = 120f;
                p.pivotMinSpeed = 1.8f;
                p.pivotDeceleration = 9f;
                p.inputSmoothing = 0.12f;
                p.gravity = -32f;
                EditorUtility.SetDirty(p);
            }
            if (p.cameraDistanceOffset == 0f) { p.cameraDistanceOffset = 4f; EditorUtility.SetDirty(p); }
            if (Mathf.Approximately(p.aimTurnSpeed, 720f)) { p.aimTurnSpeed = 240f; p.aimMoveSpeedFactor = 0.5f; EditorUtility.SetDirty(p); }
            return p;
        }

        // ---------- プレイヤープレハブ ----------
        static GameObject CreatePlayerPrefab(MovementProfile human, MovementProfile armor)
        {
            EnsureFolder("Assets/_Project/Prefabs");
            var root = new GameObject("Player");
            root.tag = "Player";

            var cc = root.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0, 0.9f, 0);
            cc.stepOffset = 0.35f;
            cc.slopeLimit = 45f;
            cc.skinWidth = 0.04f;
            cc.minMoveDistance = 0f;

            var motor = root.AddComponent<CharacterMotor>();
            SetField(motor, "profile", human);

            var reader = root.AddComponent<PlayerInputReader>();
            SetField(reader, "actions", AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputPath));

            var ctrl = root.AddComponent<PlayerController>();
            SetField(ctrl, "input", reader);

            var dbg = root.AddComponent<PlayerDebugTools>();
            SetField(dbg, "motor", motor);
            SetField(dbg, "input", reader);
            SetField(dbg, "controller", ctrl);
            var so = new SerializedObject(dbg);
            var arr = so.FindProperty("profiles");
            arr.arraySize = 2;
            arr.GetArrayElementAtIndex(0).objectReferenceValue = human;
            arr.GetArrayElementAtIndex(1).objectReferenceValue = armor;
            so.ApplyModifiedPropertiesWithoutUndo();

            // 見た目：Visual（傾き用の親）の下にモデルを置く。当たり判定は親の CharacterController。
            // モデルを差し替えるときは HeroSetup.HeroFbx を変えるだけでよい（Humanoid なのでアニメーションは共通）。
            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            HeroSetup.Setup();
            var model = (GameObject)PrefabUtility.InstantiatePrefab(HeroSetup.HeroModel);
            model.name = "Model";
            model.transform.SetParent(visual.transform, false);
            var animator = model.GetComponent<Animator>();
            if (!animator) animator = model.AddComponent<Animator>();
            animator.runtimeAnimatorController = HeroSetup.Controller;
            animator.avatar = HeroSetup.HeroAvatar;
            animator.applyRootMotion = false;   // 移動は Motor が担当。アニメーションは見た目だけ
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>())
                smr.updateWhenOffscreen = true;

            // M5: 武器（拳銃）を持つ・しまう・構える
            var holder = root.AddComponent<WeaponHolder>();
            SetField(holder, "animator", animator);
            // 持ち物：1=拳銃、2=ライフル
            var hso = new SerializedObject(holder);
            var lo = hso.FindProperty("loadout");
            lo.arraySize = 2;
            lo.GetArrayElementAtIndex(0).objectReferenceValue = CreatePistol();
            lo.GetArrayElementAtIndex(1).objectReferenceValue = CreateRifle();
            hso.ApplyModifiedPropertiesWithoutUndo();

            // M6: 射撃と近接攻撃
            var shooter = root.AddComponent<WeaponShooter>();
            SetField(shooter, "holder", holder);
            SetField(shooter, "motor", motor);
            var melee = root.AddComponent<MeleeAttack>();
            SetField(melee, "animator", animator);
            SetField(melee, "motor", motor);
            SetField(ctrl, "shooter", shooter);
            SetField(ctrl, "melee", melee);

            // M7: 体力・被弾反応・死亡と復活
            var health = root.AddComponent<Health>();
            var hso2 = new SerializedObject(health);
            hso2.FindProperty("maxHealth").floatValue = 100f;
            hso2.FindProperty("faction").enumValueIndex = (int)Faction.Player;
            hso2.ApplyModifiedPropertiesWithoutUndo();
            var reaction = root.AddComponent<HitReaction>();
            reaction.staggerTime = 0.22f;
            reaction.knockbackScale = 0.8f;
            reaction.flash = false;
            root.AddComponent<PlayerLife>();
            root.AddComponent<DodgeRoll>(); // M8: 回避（前転）

            // パワーアーマー：装甲部品を骨に付けておき（普段は非表示）、乗った時だけ表示する
            var pieces = PowerArmorSetup.Fit(model, HeroSetup.HeroAvatar);
            foreach (var p in pieces) p.SetActive(false);
            var footsteps = root.AddComponent<Matoi.Equipment.PowerArmorFootsteps>();
            SetField(footsteps, "animator", animator);
            SetField(footsteps, "motor", motor);
            var ram = root.AddComponent<Matoi.Equipment.PowerArmorRam>();
            SetField(ram, "motor", motor);
            var armorUser = root.AddComponent<Matoi.Equipment.PowerArmorUser>();
            var aso = new SerializedObject(armorUser);
            var ap = aso.FindProperty("armorPieces");
            ap.arraySize = pieces.Count;
            for (int i = 0; i < pieces.Count; i++) ap.GetArrayElementAtIndex(i).objectReferenceValue = pieces[i];
            var hide = model.GetComponentsInChildren<Renderer>()
                .Where(r => r.name.Contains("Hair") || r.name.Contains("Beard") || r.name.Contains("Eyebrows")).ToArray();
            var hp = aso.FindProperty("hideWhenArmored");
            hp.arraySize = hide.Length;
            for (int i = 0; i < hide.Length; i++) hp.GetArrayElementAtIndex(i).objectReferenceValue = hide[i];
            aso.FindProperty("visualRoot").objectReferenceValue = visual.transform;
            aso.FindProperty("armorProfile").objectReferenceValue = armor;
            aso.ApplyModifiedPropertiesWithoutUndo();
            var lineGo = new GameObject("AimLine");
            lineGo.transform.SetParent(root.transform, false);
            var line = lineGo.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.widthMultiplier = 0.035f;
            line.numCapVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.sharedMaterial = AimLineMaterial();
            line.startColor = new Color(1f, 0.35f, 0.2f, 0.75f);
            line.endColor = new Color(1f, 0.35f, 0.2f, 0f);
            SetField(holder, "aimLine", line);
            SetField(ctrl, "weapons", holder);

            var charAnim = root.AddComponent<CharacterAnimator>();
            SetField(charAnim, "motor", motor);
            SetField(charAnim, "animator", animator);
            SetField(charAnim, "leanRoot", visual.transform);
            SetField(melee, "characterAnimator", charAnim);
            var caSo = new SerializedObject(charAnim);
            caSo.FindProperty("sprintClipSpeed").floatValue = HeroSetup.LastSpeeds.sprint;
            caSo.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        // ---------- 武器 ----------
        const string PistolFbx = "Assets/_Project/Art/Weapons/Pistol/Pistol.fbx";
        const string PistolPrefab = "Assets/_Project/Prefabs/Weapons/Pistol.prefab";
        const string PistolData = "Assets/_Project/Data/Weapons/Weapon_Pistol.asset";

        const string RifleFbx = "Assets/_Project/Art/Weapons/AK47/AK47.fbx";
        const string RifleTex = "Assets/_Project/Art/Weapons/AK47/Textures";
        const string RiflePrefab = "Assets/_Project/Prefabs/Weapons/AK47.prefab";
        const string RifleData = "Assets/_Project/Data/Weapons/Weapon_AK47.asset";

        /// <summary>モデル（FBX）から、Weapon 部品付きの武器プレハブを作る。</summary>
        static Weapon CreateWeaponPrefab(string fbx, string prefabPath, string name)
        {
            EnsureFolder("Assets/_Project/Prefabs/Weapons");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var root = new GameObject(name);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.transform.SetParent(root.transform, false);
            foreach (var r in inst.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            var weapon = root.AddComponent<Weapon>();
            Transform muzzle = null;
            foreach (var t in inst.GetComponentsInChildren<Transform>())
            {
                if (t.name == "Muzzle") { SetField(weapon, "muzzle", t); muzzle = t; }
                if (t.name == "SupportHand") SetField(weapon, "supportHand", t);
            }
            // 銃口が +Z（前）に来るよう向きをそろえる。書き出し方によって前後が逆になることがあるため
            if (muzzle && root.transform.InverseTransformPoint(muzzle.position).z < 0f)
                inst.transform.localRotation = Quaternion.Euler(0f, 180f, 0f) * inst.transform.localRotation;
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            return prefab.GetComponent<Weapon>();
        }

        static WeaponDefinition LoadOrCreateWeapon(string path, out bool created)
        {
            EnsureFolder("Assets/_Project/Data/Weapons");
            var def = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(path);
            created = def == null;
            if (created)
            {
                def = ScriptableObject.CreateInstance<WeaponDefinition>();
                AssetDatabase.CreateAsset(def, path);
            }
            return def;
        }

        static WeaponDefinition CreatePistol()
        {
            var def = LoadOrCreateWeapon(PistolData, out _);
            def.prefab = CreateWeaponPrefab(PistolFbx, PistolPrefab, "Pistol");
            EditorUtility.SetDirty(def);
            return def;
        }

        static WeaponDefinition CreateRifle()
        {
            // 材質（4K テクスチャ → 取り込み時に 2K へ縮小）
            EnsureFolder("Assets/_Project/Art/Weapons/AK47/Materials");
            const string matPath = "Assets/_Project/Art/Weapons/AK47/Materials/M_AK47.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{RifleTex}/T_AK47_BaseColor.png"));
            mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{RifleTex}/T_AK47_Normal.png"));
            mat.EnableKeyword("_NORMALMAP");
            mat.SetTexture("_OcclusionMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{RifleTex}/T_AK47_AO.png"));
            mat.EnableKeyword("_OCCLUSIONMAP");
            mat.SetFloat("_Metallic", 0.35f);
            mat.SetFloat("_Smoothness", 0.4f);
            EditorUtility.SetDirty(mat);
            var importer = (ModelImporter)AssetImporter.GetAtPath(RifleFbx);
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "AK"), mat);
            importer.SaveAndReimport();

            var def = LoadOrCreateWeapon(RifleData, out bool created);
            if (created)
            {
                def.displayName = "AK47";
                def.stance = WeaponStance.Rifle;
                def.holster = HolsterSlot.Back;
                def.visualScale = 1.0f;                                    // 実寸 0.89m で十分見える
                def.shoulderOffset = new Vector3(-0.08f, -0.07f, 0.15f);   // 銃床を右肩に当てる
                def.rightWristOffset = new Vector3(0.025f, -0.035f, -0.05f);
                def.leftWristOffset = new Vector3(-0.03f, -0.05f, -0.18f); // ハンドガードの手前側を握る
                def.aimMoveSpeedMultiplier = 0.85f;
                def.aimLineLength = 14f;
                def.damage = 28f;
                def.fireRate = 10f;
                def.automatic = true;
                def.range = 45f;
                def.spread = 1.5f;
                def.moveSpread = 4f;
                def.knockback = 1.6f;
                def.magazineSize = 30;
                def.reserveAmmo = 120;
                def.reloadTime = 1.8f;
                def.recoilKick = 0.035f;
                def.recoilPitch = 5f;
                def.recoilShake = 0.09f;
                def.sound = ShotSound.Rifle;
            }
            def.prefab = CreateWeaponPrefab(RifleFbx, RiflePrefab, "AK47");
            EditorUtility.SetDirty(def);
            return def;
        }

        /// <summary>戦闘演出の材質を作り、シーンに CombatFx を置く。</summary>
        public static void InstallCombatFx()
        {
            var old = GameObject.Find("CombatFx");
            if (old) Object.DestroyImmediate(old);
            var go = new GameObject("CombatFx");
            var fx = go.AddComponent<CombatFx>();
            SetField(fx, "flashMaterial", UnlitHdr("M_FX_MuzzleFlash", new Color(4f, 1.9f, 0.45f)));
            SetField(fx, "tracerMaterial", UnlitHdr("M_FX_Tracer", new Color(5f, 3.5f, 1.5f)));
            SetField(fx, "sparkMaterial", ParticleMaterial());
        }

        static Material UnlitHdr(string name, Color hdr)
        {
            string path = $"Assets/_Project/Art/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", hdr);   // 1 より明るい色 → ブルームで光る
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material ParticleMaterial()
        {
            const string path = "Assets/_Project/Art/Materials/M_FX_Particle.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                AssetDatabase.CreateAsset(m, path);
            }
            // 半透明（アルファブレンド）
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material AimLineMaterial()
        {
            const string path = "Assets/_Project/Art/Materials/M_AimLine.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Sprites/Default"));
                AssetDatabase.CreateAsset(m, path);
            }
            return m;
        }

        // ---------- カメラ（Cinemachine。細かい調整は M3） ----------
        static void SetupCamera(Transform target)
        {
            var mainCam = Camera.main;
            if (mainCam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                mainCam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
                go.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
            }
            if (!mainCam.GetComponent<CinemachineBrain>()) mainCam.gameObject.AddComponent<CinemachineBrain>();

            var rot = Quaternion.Euler(CameraPitch, CameraYaw, 0f);
            var focus = target.position + Vector3.up * 1.0f;
            mainCam.transform.SetPositionAndRotation(focus - rot * Vector3.forward * CameraDistance, rot);

            var cmGo = new GameObject("CM PlayerCamera");
            cmGo.transform.SetPositionAndRotation(mainCam.transform.position, rot);
            var cm = cmGo.AddComponent<CinemachineCamera>();
            cm.Follow = target;
            var lens = cm.Lens;
            lens.FieldOfView = 35f;
            lens.NearClipPlane = 0.5f;
            lens.FarClipPlane = 600f;
            cm.Lens = lens;

            var composer = cmGo.AddComponent<CinemachinePositionComposer>();
            composer.CameraDistance = CameraDistance;
            composer.TargetOffset = new Vector3(0, 1.0f, 0);
            composer.Damping = new Vector3(0.45f, 0.45f, 0.45f);
            // M3: 進行方向の少し先を映す（先読み）。走っている方向の視界が広くなる
            composer.Lookahead = new LookaheadSettings { Enabled = true, Time = 0.45f, Smoothing = 8f, IgnoreY = true };

            // M3: 走る速さ・パワーアーマーに応じた距離調整
            var pcc = cmGo.AddComponent<PlayerCameraController>();
            SetField(pcc, "composer", composer);
            SetField(pcc, "motor", target.GetComponent<CharacterMotor>());
            var pso = new SerializedObject(pcc);
            pso.FindProperty("baseDistance").floatValue = CameraDistance;
            pso.ApplyModifiedPropertiesWithoutUndo();

            // M3: 画面揺れ（受け手：カメラ、送り手：CameraShake）
            cmGo.AddComponent<CinemachineImpulseListener>();
            var shakeGo = new GameObject("CameraShake");
            shakeGo.transform.SetParent(cmGo.transform, false);
            var src = shakeGo.AddComponent<CinemachineImpulseSource>();
            src.ImpulseDefinition.ImpulseShape = CinemachineImpulseDefinition.ImpulseShapes.Bump;
            src.ImpulseDefinition.ImpulseDuration = 0.25f;
            src.DefaultVelocity = new Vector3(0.15f, -0.5f, 0f);
            shakeGo.AddComponent<CameraShake>();

            // M3: プレイヤーを隠す家・木を透過
            var hider = mainCam.GetComponent<OcclusionHider>();
            if (!hider) hider = mainCam.gameObject.AddComponent<OcclusionHider>();
            hider.Target = target;
        }

        // ---------- ヘルパー ----------
        static void SetField(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null) { Debug.LogError($"[Matoi] field not found: {target.GetType().Name}.{field}"); return; }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
