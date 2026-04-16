Shader "Custom/LightningGlowMobile" {
	Properties {
		_CoreColor ("Core Color", Vector) = (1,1,1,1)
		_GlowColor ("Glow Color", Vector) = (0.5,0.8,1,0.5)
		_CoreWidth ("Core Width", Range(0, 1)) = 0.3
		_GlowWidth ("Glow Width", Range(0, 1)) = 1
		_GlowOffset ("Glow Offset", Range(-1, 1)) = 0
		_CoreHardness ("Core Hardness", Range(0.01, 10)) = 1
		_GlowHardness ("Glow Hardness", Range(0.01, 10)) = 1
		_StartWidthMultiplier ("Start Width Multiplier", Range(0, 50)) = 1
		_EndWidthMultiplier ("End Width Multiplier", Range(0, 50)) = 1
		_StartWidthDistance ("Start Width Distance (World)", Float) = 1
		_EndWidthDistance ("End Width Distance (World)", Float) = 1
		_SplineLength ("Spline Length (World)", Float) = 10
		_DistortionNoiseTex ("Distortion Noise Texture", 2D) = "white" {}
		_DistortionStrength ("Distortion Strength", Float) = 0.05
		_DistortionSpeed ("Distortion Speed", Float) = 2
		_ThicknessNoiseTex ("Thickness Noise Texture", 2D) = "white" {}
		_ThicknessStrengthMin ("Thickness Strength Min", Float) = 0.5
		_ThicknessStrengthMax ("Thickness Strength Max", Float) = 1.5
		_ThicknessSpeed ("Thickness Speed", Float) = 1
		_TimeScale ("Time Scale", Float) = 1
		_NoiseScale ("Noise Scale (Base Length)", Float) = 10
		[Header(Effect Toggles)] [Toggle] _EnableDistortion ("Enable Distortion", Float) = 1
		[Toggle] _EnableThicknessDistortion ("Enable Thickness Distortion", Float) = 1
		[Toggle] _EnableStartEndWidth ("Enable Start End Width", Float) = 1
	}
	//DummyShaderTextExporter
	SubShader{
		Tags { "RenderType" = "Opaque" }
		LOD 200

		Pass
		{
			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag

			float4x4 unity_ObjectToWorld;
			float4x4 unity_MatrixVP;

			struct Vertex_Stage_Input
			{
				float4 pos : POSITION;
			};

			struct Vertex_Stage_Output
			{
				float4 pos : SV_POSITION;
			};

			Vertex_Stage_Output vert(Vertex_Stage_Input input)
			{
				Vertex_Stage_Output output;
				output.pos = mul(unity_MatrixVP, mul(unity_ObjectToWorld, input.pos));
				return output;
			}

			float4 frag(Vertex_Stage_Output input) : SV_TARGET
			{
				return float4(1.0, 1.0, 1.0, 1.0); // RGBA
			}

			ENDHLSL
		}
	}
	Fallback "Mobile/Particles/Alpha Blended"
}