﻿Shader "PBM/Quadrilateral" {

    Properties{
        _MainTex("Base (RGB)", 2D) = "white" {}
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("ZTest", Float) = 4
    }

        SubShader{
        Tags {"Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent"}
        Blend SrcAlpha OneMinusSrcAlpha
        ZTest[_ZTest]

            pass {
                CGPROGRAM

                #pragma vertex vert          
                #pragma fragment frag
                #include "UnityCG.cginc"

                uniform sampler2D _MainTex;

                struct vertexInput {
                    float4 vertex : POSITION;
                    
                    float3 texcoord  : TEXCOORD0;
                };

                struct vertexOutput {
                    float4 pos : SV_POSITION;

                    float3 uv  : TEXCOORD0;
                };

                vertexOutput vert(vertexInput input)
                {
                    vertexOutput output;
                    output.pos = UnityObjectToClipPos(input.vertex);

                    output.uv = input.texcoord;

                    return output;
                }

                float4 frag(vertexOutput i) : COLOR
                {
                    return tex2D(_MainTex, i.uv.xy / i.uv.z);
                }

                ENDCG
            }
    }
}