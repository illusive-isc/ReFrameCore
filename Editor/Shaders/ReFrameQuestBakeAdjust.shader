// 焼き上がった Toon Lit のメインテクスチャに、最後の調整をかけるためだけのシェーダー。
// 画面には出さない (RenderTexture への Blit 専用)。
//
// やることは 3 つ。
//
// 1. 影色 (_UseShadowColor)
//    lilToon は<b>アルベドに影色を掛けて</b>陰影を出す。Toon Lit にはその仕組みが無いので、
//    そのまま持っていくと全体が明るく・彩度が高く見える。影色を焼き込んでおけば、
//    肌はピンク寄り、髪はグレー寄りに沈む、といった<b>色味の違いまで</b>再現できる。
//    法線マップがあれば向きに応じて 1 影 / 2 影を塗り分け、無ければ
//    「だいたいこのくらい陰る」という一定量 (_FlatLuminance) で塗る。
//
// 2. 明度補正 (_Brightness)
//    影色で足りないぶんの一律の調整。影色を入れたら 1 のままで足りることが多い。
//
// 3. 法線マップからの陰影 (_UseShadow)
//    Toon Lit は _MainTex しか持てず法線マップは捨てられる。捨てる前に凹凸を焼き込む。
Shader "Hidden/ReFrame/QuestBakeAdjust"
{
    Properties
    {
        _MainTex ("Main", 2D) = "white" {}
        _BumpMap ("Normal", 2D) = "bump" {}
        _Brightness ("Brightness", Range(0, 1)) = 1
        _Tint ("Tint (RGB multiplier)", Color) = (1,1,1,1)
        _UseShadow ("Use Normal Shadow", Int) = 0
        _ShadowStrength ("Shadow Strength", Range(0, 1)) = 0.5
        _ShadowBorder ("Shadow Border", Range(0, 1)) = 0.5
        _ShadowBorderBlur ("Shadow Border Blur", Range(0, 1)) = 0.2

        _UseShadowColor ("Use Shadow Color", Int) = 0
        _ShadowColor1 ("Shadow Color 1st", Color) = (1,1,1,1)
        _ShadowColor2 ("Shadow Color 2nd", Color) = (1,1,1,1)
        _ShadowBorder1 ("Shadow Border 1st", Range(0, 1)) = 0.5
        _ShadowBorder2 ("Shadow Border 2nd", Range(0, 1)) = 0.25
        _ShadowBlur1 ("Shadow Blur 1st", Range(0, 1)) = 0.3
        _ShadowBlur2 ("Shadow Blur 2nd", Range(0, 1)) = 0.6
        _FlatLuminance ("Flat Luminance", Range(0, 1)) = 0.62
        _ShadowMix ("Shadow Mix", Range(0, 1)) = 1
    }
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _BumpMap;
            float _Brightness;
            float4 _Tint;
            uint _UseShadow;
            float _ShadowStrength;
            float _ShadowBorder;
            float _ShadowBorderBlur;

            uint _UseShadowColor;
            float4 _ShadowColor1;
            float4 _ShadowColor2;
            float _ShadowBorder1;
            float _ShadowBorder2;
            float _ShadowBlur1;
            float _ShadowBlur2;
            float _FlatLuminance;
            float _ShadowMix;

            // 陰影を作るための仮の光源。斜め上手前から当てる。
            static const float3 ReFrameDummyLight = float3(-0.5, 0.5, 1.05);

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            // 法線から明るさ (0..1) を作る。ランバートに、鋭いスペキュラを少しだけ足す。
            float NormalToLuminance(float3 normal)
            {
                float3 light = normalize(ReFrameDummyLight);
                float lambert = max(0, dot(normal, light));
                float3 reflected = reflect(-light, normal);
                float specular = pow(max(0, dot(reflected, float3(0, 0, 1))), 100);
                return saturate(lambert + specular);
            }

            // 明るさをトゥーン的な 2 段に落とす。境界の前後だけ滑らかに繋ぐ。
            float ToonShadow(float luminance)
            {
                float upper = saturate(_ShadowBorder - _ShadowBorderBlur / 2);
                float lower = saturate(_ShadowBorder + _ShadowBorderBlur / 2);
                float dark = 1 - _ShadowStrength;
                if (luminance > upper)
                    return 1;
                if (luminance >= lower && upper > lower)
                    return lerp(dark, 1, (luminance - lower) / (upper - lower));
                return dark;
            }

            // lilToon の 1 影 / 2 影を、明るさから選び分けて色として返す。
            float3 ShadowTint(float luminance)
            {
                // border を跨ぐところで滑らかに切り替える。blur は幅。
                float lit = smoothstep(
                    _ShadowBorder1 - _ShadowBlur1 * 0.5,
                    _ShadowBorder1 + _ShadowBlur1 * 0.5,
                    luminance);
                float mid = smoothstep(
                    _ShadowBorder2 - _ShadowBlur2 * 0.5,
                    _ShadowBorder2 + _ShadowBlur2 * 0.5,
                    luminance);
                float3 tint = lerp(_ShadowColor2.rgb, _ShadowColor1.rgb, mid);
                tint = lerp(tint, float3(1, 1, 1), lit);
                return lerp(float3(1, 1, 1), tint, _ShadowMix);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv);

                // 法線マップがあれば向きから明るさを作る。無ければ一定量とみなす。
                float luminance = _FlatLuminance;
                if (_UseShadow)
                {
                    float3 normal = UnpackNormal(tex2D(_BumpMap, i.uv));
                    luminance = NormalToLuminance(normal);
                    col.rgb *= ToonShadow(luminance);
                }

                if (_UseShadowColor)
                    col.rgb *= ShadowTint(luminance);

                col.rgb *= _Brightness;
                col.rgb *= _Tint.rgb;
                return col;
            }
            ENDCG
        }
    }
}
