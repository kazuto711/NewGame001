// 『纏』用のトゥーンシェーダー（URP）。
//  - 光と影を数段階にくっきり分ける（ランプ）。影の色は青みに寄せる
//  - 縁の光（リムライト）、小さな硬いハイライト
//  - 背面を膨らませる方式のアウトライン（画面上の太さが一定）
//  - 影・霧・SSAO・追加ライト・アルファ切り抜きに対応
// プロパティ名は URP/Lit と同じ（_BaseMap, _BaseColor, _BumpMap ...）なので、シェーダーを差し替えるだけで移行できる。
Shader "Matoi/Toon"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1,1,1,1)
        _Cutoff("Alpha Cutoff", Range(0,1)) = 0.5
        [Toggle(_ALPHATEST_ON)] _AlphaClip("Alpha Clip", Float) = 0
        [Normal] _BumpMap("Normal Map", 2D) = "bump" {}
        _OcclusionMap("Occlusion", 2D) = "white" {}
        [HDR] _EmissionColor("Emission", Color) = (0,0,0,1)
        _Smoothness("Smoothness (ハイライトの強さ)", Range(0,1)) = 0.3
        _Metallic("Metallic", Range(0,1)) = 0

        [Header(Toon)]
        _DirectStrength("日なたの明るさ", Range(0,2)) = 0.6
        _AmbientStrength("環境光の明るさ", Range(0,2)) = 1.0
        _RampThreshold("影の境目", Range(-1,1)) = 0.05
        _RampSmooth("境目のぼかし", Range(0.001,0.5)) = 0.04
        _ShadeColor("影の色味", Color) = (0.72, 0.76, 0.95, 1)
        _ShadeStrength("影の濃さ", Range(0,1)) = 0.55
        _RimColor("リムライト色", Color) = (1.0, 0.9, 0.75, 1)
        _RimStrength("リムライト強さ", Range(0,1)) = 0.25
        _RimPower("リムライト幅", Range(0.5,8)) = 3.5

        [Header(Outline)]
        _OutlineWidth("アウトライン太さ (px)", Range(0,6)) = 1.6
        _OutlineColor("アウトライン色", Color) = (0.08, 0.07, 0.07, 1)

        [HideInInspector] _Cull("Cull", Float) = 2
        [HideInInspector] _Surface("Surface", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
        TEXTURE2D(_OcclusionMap); SAMPLER(sampler_OcclusionMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _Cutoff;
            half4 _EmissionColor;
            half _Smoothness;
            half _Metallic;
            half _DirectStrength;
            half _AmbientStrength;
            half _RampThreshold;
            half _RampSmooth;
            half4 _ShadeColor;
            half _ShadeStrength;
            half4 _RimColor;
            half _RimStrength;
            half _RimPower;
            half _OutlineWidth;
            half4 _OutlineColor;
        CBUFFER_END

        half4 SampleBase(float2 uv)
        {
            return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv) * _BaseColor;
        }

        void AlphaClip(half a)
        {
            #if defined(_ALPHATEST_ON)
                clip(a - _Cutoff);
            #endif
        }
        ENDHLSL

        // ------------------------------------------------------------------ 本体
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _EMISSION
            #pragma shader_feature_local_fragment _OCCLUSIONMAP
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half4 tangentWS : TEXCOORD3;
                half fog : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(v.normalOS, v.tangentOS);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = n.normalWS;
                o.tangentWS = half4(n.tangentWS, v.tangentOS.w * GetOddNegativeScale());
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half Ramp(half x) { return smoothstep(_RampThreshold - _RampSmooth, _RampThreshold + _RampSmooth, x); }

            half4 Frag(Varyings i, bool facing : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half4 base = SampleBase(i.uv);
                AlphaClip(base.a);

                half3 N = normalize(i.normalWS) * (facing ? 1 : -1);
                #if defined(_NORMALMAP)
                    half3 tn = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv));
                    half3 bitangent = i.tangentWS.w * cross(i.normalWS, i.tangentWS.xyz);
                    N = normalize(TransformTangentToWorld(tn, half3x3(i.tangentWS.xyz, bitangent, i.normalWS)));
                #endif
                half3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);

                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalWS = N;
                inputData.viewDirectionWS = V;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                inputData.shadowCoord = TransformWorldToShadowCoord(i.positionWS);

                half occlusion = 1;
                #if defined(_OCCLUSIONMAP)
                    occlusion = SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, i.uv).g;
                #endif
                half directAO = 1, indirectAO = occlusion;
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(inputData.normalizedScreenSpaceUV);
                    directAO = ao.directAmbientOcclusion;
                    indirectAO *= ao.indirectAmbientOcclusion;
                #endif

                // ---- 主光源：段階的な陰影 ----
                Light mainLight = GetMainLight(inputData.shadowCoord, i.positionWS, half4(1,1,1,1));
                half NdotL = dot(N, mainLight.direction);
                half shadow = smoothstep(0.35, 0.65, mainLight.shadowAttenuation);
                half lit = min(Ramp(NdotL), shadow) * directAO;

                half3 albedo = base.rgb * (1 - _Metallic * 0.5);
                half3 ambient = SampleSH(N) * indirectAO * _AmbientStrength;
                // 影側：環境光 × 影の色味（真っ黒にしない）
                half3 shadeTint = lerp(half3(1,1,1), _ShadeColor.rgb, _ShadeStrength);
                half3 color = albedo * ambient * shadeTint;
                // 日なた：面の向きでわずかに明暗を残す（完全にベタにすると立体感が消える）
                half slope = lerp(0.75, 1.0, saturate(NdotL));
                color += albedo * mainLight.color * lit * slope * _DirectStrength;

                // 小さく硬いハイライト（濡れた物・金属）
                half3 H = normalize(mainLight.direction + V);
                half spec = step(1 - (0.02 + _Smoothness * 0.06), dot(N, H)) * _Smoothness * lit;
                color += mainLight.color * spec * lerp(0.4, 1.0, _Metallic);

                // ---- 追加ライト（マズルフラッシュ等） ----
                #if defined(_ADDITIONAL_LIGHTS)
                    uint count = GetAdditionalLightsCount();
                    #if USE_CLUSTER_LIGHT_LOOP
                    [loop] for (uint li = 0; li < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); li++)
                    {
                        Light l = GetAdditionalLight(li, i.positionWS, half4(1,1,1,1));
                        color += albedo * l.color * Ramp(dot(N, l.direction)) * l.distanceAttenuation * l.shadowAttenuation;
                    }
                    #endif
                    LIGHT_LOOP_BEGIN(count)
                        Light l = GetAdditionalLight(lightIndex, i.positionWS, half4(1,1,1,1));
                        color += albedo * l.color * Ramp(dot(N, l.direction)) * l.distanceAttenuation * l.shadowAttenuation;
                    LIGHT_LOOP_END
                #endif

                // ---- 縁の光 ----
                half rim = pow(saturate(1 - dot(N, V)), _RimPower);
                color += _RimColor.rgb * rim * _RimStrength * (0.35 + 0.65 * lit) * mainLight.color;

                #if defined(_EMISSION)
                    color += _EmissionColor.rgb;
                #endif

                // 霧は画素ごとに計算する（巨大な地面の頂点だけで計算すると全体が霞むため）
                color = MixFog(color, ComputeFogFactor(TransformWorldToHClip(i.positionWS).z));
                return half4(color, 1);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------ アウトライン（背面を膨らませる）
        Pass
        {
            Name "Outline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half fog : TEXCOORD1; };

            Varyings Vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                float4 cs = TransformObjectToHClip(v.positionOS.xyz);
                float3 nWS = TransformObjectToWorldNormal(v.normalOS);
                float2 nCS = mul((float3x3)GetWorldToHClipMatrix(), nWS).xy;
                float len = max(length(nCS), 1e-4);
                // 画面上で一定の太さ（px）になるよう、w を掛けて押し出す
                cs.xy += (nCS / len) * (_OutlineWidth * 2.0 / _ScreenParams.xy) * cs.w;
                o.positionCS = cs;
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.fog = ComputeFogFactor(cs.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half4 base = SampleBase(i.uv);
                AlphaClip(base.a);
                if (_OutlineWidth <= 0.001) discard;
                half3 c = lerp(_OutlineColor.rgb, base.rgb * _OutlineColor.rgb * 2.0, 0.35);
                return half4(MixFog(c, i.fog), 1);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------ 影を落とす
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 pWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 nWS = TransformObjectToWorldNormal(v.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 L = normalize(_LightPosition - pWS);
                #else
                    float3 L = _LightDirection;
                #endif
                o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(pWS, nWS, L)));
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }
            half4 Frag(Varyings i) : SV_Target { AlphaClip(SampleBase(i.uv).a); return 0; }
            ENDHLSL
        }

        // ------------------------------------------------------------------ 深度（SSAO・被写界深度などが使う）
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(Attributes v) { Varyings o; UNITY_SETUP_INSTANCE_ID(v); o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.uv = TRANSFORM_TEX(v.uv, _BaseMap); return o; }
            half Frag(Varyings i) : SV_Target { AlphaClip(SampleBase(i.uv).a); return i.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half3 normalWS : TEXCOORD1; };
            Varyings Vert(Attributes v)
            {
                Varyings o; UNITY_SETUP_INSTANCE_ID(v);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                AlphaClip(SampleBase(i.uv).a);
                return half4(NormalizeNormalPerPixel(i.normalWS), 0);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
