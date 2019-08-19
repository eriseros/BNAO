Shader "Hidden/BNAO"
{
	SubShader
	{
		Pass
		{
			Cull Off
			// Blend SrcAlpha OneMinusSrcAlpha
			CGPROGRAM
				#pragma vertex vert
				#pragma fragment frag
				#pragma multi_compile MODE_BN MODE_AO

				#include "UnityCG.cginc"
				#include "Autolight.cginc"

				struct v2f
				{
					float4 vertex 	: SV_POSITION;
					float2 texcoord : TEXCOORD1;
					float3 worldPos : TEXCOORD2;
				};

				v2f vert (appdata_full v)
				{
					v2f o;
					UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
					o.vertex 	= UnityObjectToClipPos(v.vertex);
					o.texcoord 	= v.texcoord;
					o.worldPos 	= mul(unity_ObjectToWorld, v.vertex).xyz;

					return o;
				}

				UNITY_DECLARE_SHADOWMAP(_ShadowMap);
				float4x4 	_WorldToShadow;

				sampler2D 	_PositionCache;
				sampler2D 	_NormalCache;

				sampler2D 	_PrevTex;
				float4	 	_PrevTex_TexelSize;

				float 		_Samples;
				float 		_Sample;
				float3 		_Dir;
				float 		_ShadowBias;
				float		_ClampToHemisphere;

				fixed4 frag (v2f i) : SV_Target
				{
					float2 uv = i.texcoord;
					// uv += _Dir.xy * _PrevTex_TexelSize.xy * 0.5;
					float4 positionTex = tex2D(_PositionCache, uv);
					float3 worldPos = positionTex.xyz;
					float3 normal 	= normalize(tex2D(_NormalCache, uv));

					// worldPos += normal * _ShadowBias;
					worldPos += normal * _ShadowBias;

					float3 lightPos = mul(_WorldToShadow, float4(worldPos, 1.0)).xyz;

					float shadow = UNITY_SAMPLE_SHADOW(_ShadowMap, lightPos);

					float3 prevColor = tex2D(_PrevTex, i.texcoord);

					#if MODE_AO
						if (_ClampToHemisphere == 1.0 && dot(normal, _Dir) <= 0)
							shadow = 0;

						float3 color = shadow;

						float alpha = 1.0 / (1 + _Sample);

						color = lerp(prevColor, color, alpha);
					#elif MODE_BN
						if (_ClampToHemisphere == 1.0 && dot(normal, _Dir) <= 0)
							shadow = 0;

						float3 color = _Dir;

						color = prevColor + color * shadow;// * (1-dot(normal, _Dir));
					#endif

					return float4(color, positionTex.a);
				}
			ENDCG 
		}
	} 	
}