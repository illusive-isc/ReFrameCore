// 何も描かないだけのシェーダー。プレビュー専用で、ビルド結果には一切出てこない。
//
// NDMF のプレビューは「マテリアル枠を差し替える」ことしかできず、枠を減らせない。
// ビルドでは消える余剰マテリアルスロット (lilToon の偽影など) を
// プレビューでも見えなくするために、この「描かないマテリアル」を差し込む。
//
// ColorMask 0 で色を書かず、ZWrite Off で深度も汚さない。
Shader "Hidden/ReFrame/PreviewHidden"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Cull Off
            ZWrite Off
            ColorMask 0

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 vert(float4 vertex : POSITION) : SV_POSITION
            {
                return UnityObjectToClipPos(vertex);
            }

            fixed4 frag() : SV_Target
            {
                return fixed4(0, 0, 0, 0);
            }
            ENDCG
        }
    }
}
