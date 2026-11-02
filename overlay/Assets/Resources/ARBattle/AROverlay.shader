// Overlay for the AR battle (ARBattleRig): detected tables as a dot grid, and the board outline shown before
// placing it. Lives in Resources so the player build always includes it.
// Colour = _Color * (vertex colour if _VertexColor is 1). With _DotSpacing > 0 it adds round dots of
// _DotColor on a grid of that spacing in UV units (AR Foundation gives plane meshes UVs in metres).
Shader "Hidden/CardWars/AROverlay"
{
	Properties
	{
		_Color ("Fill", Color) = (1, 1, 1, 1)
		_DotColor ("Dots", Color) = (1, 1, 1, 0)
		_DotSpacing ("Dot spacing (UV units, 0 = no dots)", Float) = 0
		_DotRadius ("Dot radius (fraction of the spacing)", Float) = 0.15
		_VertexColor ("Use vertex colour", Float) = 0
	}
	SubShader
	{
		Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
		Blend SrcAlpha OneMinusSrcAlpha
		ZWrite Off
		Cull Off
		Lighting Off

		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"

			fixed4 _Color;
			fixed4 _DotColor;
			float _DotSpacing;
			float _DotRadius;
			float _VertexColor;

			struct appdata
			{
				float4 vertex : POSITION;
				float2 uv : TEXCOORD0;
				fixed4 color : COLOR;
			};

			struct v2f
			{
				float4 pos : SV_POSITION;
				float2 uv : TEXCOORD0;
				fixed4 color : COLOR;
			};

			v2f vert(appdata v)
			{
				v2f o;
				o.pos = UnityObjectToClipPos(v.vertex);
				o.uv = v.uv;
				o.color = lerp(fixed4(1, 1, 1, 1), v.color, _VertexColor) * _Color;
				return o;
			}

			fixed4 frag(v2f i) : SV_Target
			{
				fixed4 c = i.color;
				if (_DotSpacing > 0)
				{
					float2 cell = frac(i.uv / _DotSpacing) - 0.5;
					float d = length(cell);
					float aa = max(fwidth(d), 1e-4);
					float dotMask = 1 - smoothstep(_DotRadius - aa, _DotRadius + aa, d);
					c.rgb = lerp(c.rgb, _DotColor.rgb, dotMask);
					c.a = max(c.a, dotMask * _DotColor.a);
				}
				return c;
			}
			ENDCG
		}
	}
}
