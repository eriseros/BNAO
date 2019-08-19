using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class BNAO : EditorWindow
{
	[System.Serializable]
	public enum BakeMode
	{
		BentNormal,
		AmbientOcclusion
	}

	[System.Serializable]
	public enum NormalsSpace
	{
		Tangent = 0,
		Object = 1,
		World = 2
	}

	[System.Serializable]
	public enum UVChannel
	{
		UV0 = 0,
		UV1 = 1,
		UV2 = 2,
		UV3 = 3
	}

	[System.Serializable]
	public enum Resolution
	{
		_64   = 64,
		_128  = 128,
		_256  = 256,
		_512  = 512,
		_1024 = 1024,
		_2048 = 2048,
		_4096 = 4096,
		_8192 = 8192
	}

	[System.Serializable]
	public enum NameMode
	{
		Shortest,
		Longest,
		Alphabetical,
		Whatever
	}

	[System.Serializable]
	public enum CullOverrideMode
	{
		ForceTwoSided,
		ForceOneSided,
		UseMaterialParameter
	}

	public BakeMode bakeMode = BakeMode.BentNormal;
	public NormalsSpace bentNormalsSpace;
	public UVChannel uvChannel = UVChannel.UV0;
	public Resolution bakeRes = Resolution._2048;
	public Resolution shadowMapRes = Resolution._2048;
	public int samples = 1024;
	public int dilation = 32;
	[Range(0, 1)]
	public float shadowBias = 0.01f;
	[Range(0, 1)]
	public float aoBias = 0.5f;
	public bool clampToHemisphere = true;
	public bool includeScene = false;
	public bool forceSharedTexture = false;
	public bool useNormalMaps = true;
	public bool useOriginalShaders = false;
	public CullOverrideMode cullOverrideMode = CullOverrideMode.ForceTwoSided;

	public bool transparentPixels = false;
	public string outputPath = "Assets/BNAO/Bakes";
	public NameMode nameMode = NameMode.Shortest;

	bool baking = false;

    [MenuItem("Tools/BNAO")]
    static void Init ()
    {
		BNAO window  = (BNAO)EditorWindow.GetWindow(typeof(BNAO), false, "BNAO");
		window.Show();
    }

	string fullName
	{
		get
		{
			switch (bakeMode)
			{
				case BakeMode.BentNormal:
					return "Bent Normal Map";
				case BakeMode.AmbientOcclusion:
					return "Ambient Occlusion Map";
			}

			return "... wait what?!";
		}
	}

	protected void OnEnable ()
     {
         var data = EditorPrefs.GetString("BNAO", JsonUtility.ToJson(this, false));
         JsonUtility.FromJsonOverwrite(data, this);
     }
 
     protected void OnDisable ()
     {
         var data = JsonUtility.ToJson(this, false);
         EditorPrefs.SetString("BNAO", data);
     }

	void OnGUI ()
    {
		//if (samples > 5000 || bakeRes > Resolution._4096 || shadowMapRes > Resolution._4096)
		//	EditorGUILayout.HelpBox("Extreme bake settings detected, expect long bake times.", MessageType.Warning);
		bakeMode			= (BakeMode)EditorGUILayout.EnumPopup("Bake Mode", bakeMode);
		if (bakeMode != BakeMode.BentNormal) EditorGUI.BeginDisabledGroup(true);
		bentNormalsSpace	= (NormalsSpace)EditorGUILayout.EnumPopup("Normals Space", bentNormalsSpace);
		if (bakeMode != BakeMode.BentNormal) EditorGUI.EndDisabledGroup();
		uvChannel			= (UVChannel)EditorGUILayout.EnumPopup(new GUIContent("Texture Channel", "The UV channel to use when generating the output texture(s)."), uvChannel);
		bakeRes				= (Resolution)EditorGUILayout.EnumPopup(new GUIContent("Output resolution", "The resolution of the output texture(s)."), bakeRes);
		samples				= EditorGUILayout.IntSlider(new GUIContent("Sample Count", "The number of depth map samples used for each pixel."), samples, 64, 8192);
		shadowMapRes		= (Resolution)EditorGUILayout.EnumPopup(new GUIContent("Depth Map Resolution", "The resolution of the depth map. Probably only have to increase this if baking high-resolution maps for multiple, large objects."), shadowMapRes);
		dilation			= Mathf.Max(EditorGUILayout.IntField(new GUIContent("Dilation", "Adds edge padding to the output."), dilation), 0);
		shadowBias			= EditorGUILayout.Slider(new GUIContent("Depth Bias", "Depth map sampling bias. A larger value will generally give you less artifacts at the cost of loss of accuracy."), shadowBias, 0f, 1f);
		if (bakeMode != BakeMode.AmbientOcclusion) EditorGUI.BeginDisabledGroup(true);
		aoBias				= EditorGUILayout.Slider(new GUIContent("AO Bias", "Ambient Occlusion output bias. A value of 0.5 is considered neutral."), aoBias, 0f, 1f);
		if (bakeMode != BakeMode.AmbientOcclusion) EditorGUI.EndDisabledGroup();
		clampToHemisphere	= EditorGUILayout.Toggle(new GUIContent("Clamp To Hemisphere", "Discard samples that would intersect the pixel's own surface."), clampToHemisphere);
		includeScene		= EditorGUILayout.Toggle(new GUIContent("Include Scene", "If checked, will include other non-selected objects in the scene when rendering the depth map."), includeScene);
		forceSharedTexture	= EditorGUILayout.Toggle(new GUIContent("Force Shared Texture", "If checked, will render all selected objects into the same output texture. Useful if you are baking multiple objects which share a texture, but don't share materials. Objects with the same material will still be grouped even if this is not checked."), forceSharedTexture);
		if (forceSharedTexture) EditorGUI.BeginDisabledGroup(true);
		useNormalMaps		= EditorGUILayout.Toggle(new GUIContent("Use Normal Maps", "If checked, the baker will include any normal maps present on the original materials when baking. This can give a higher quality result if the bake resolution is high enough."), useNormalMaps);
		if (forceSharedTexture) EditorGUI.EndDisabledGroup();
		useOriginalShaders	= EditorGUILayout.Toggle(new GUIContent("Use Original Shaders", "Bake the depth maps using the objects' original shaders. This is useful if you are using vertex-modifying shaders, but also prevents overriding the face cull mode."), useOriginalShaders);
		if (useOriginalShaders) EditorGUI.BeginDisabledGroup(true);
		cullOverrideMode	= (CullOverrideMode)EditorGUILayout.EnumPopup(new GUIContent("Face Cull Override", "Force double or single-sided rendering. In most cases you probably want to force double-sided."), cullOverrideMode);
		if (useOriginalShaders) EditorGUI.EndDisabledGroup();

		transparentPixels	= EditorGUILayout.Toggle(new GUIContent("Transparent Background", "Whether to fill background pixels in the output texture with neutral values or leave them blank."), transparentPixels);
		GUILayout.Space(4);
		outputPath			= EditorGUILayout.TextField("Output Folder", outputPath);
		nameMode			= (NameMode)EditorGUILayout.EnumPopup(new GUIContent("Output Names", "How to determine the output file name. Only used if multiple objects which share materials are selected."), nameMode);
		var rect = GUILayoutUtility.GetLastRect();
		if (GUILayout.Button("Bake Selected Objects"))
		{
			Bake(Selection.gameObjects);
		}
    }

	public struct RendereredMesh
	{
		public Renderer renderer;
		public Mesh mesh;

		public RendereredMesh (Renderer renderer, Mesh mesh)
		{
			this.renderer = renderer;
			this.mesh = mesh;
		}
	}

	public class BNAOObject
	{
		public string name;
		public List<RendereredMesh> renderedMeshes;
		public RenderTexture positionCache;
		public RenderTexture normalCache;
		public RenderTexture result;

		public BNAOObject (string name, int bakeRes)
		{
			this.name = name;
			renderedMeshes = new List<RendereredMesh>();
			positionCache = RenderTexture.GetTemporary(bakeRes, bakeRes, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
			normalCache = RenderTexture.GetTemporary(bakeRes, bakeRes, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
			result = RenderTexture.GetTemporary(bakeRes, bakeRes, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);

			//positionCache.filterMode = FilterMode.Point;
			//normalCache.filterMode = FilterMode.Point;
			result.filterMode = FilterMode.Point;

			Graphics.SetRenderTarget(positionCache);
			GL.Clear(true, true, Color.clear);
			Graphics.SetRenderTarget(normalCache);
			GL.Clear(true, true, Color.clear);
			Graphics.SetRenderTarget(result);
			GL.Clear(true, true, Color.clear);
		}

		~BNAOObject()
		{
			RenderTexture.ReleaseTemporary(positionCache);
			RenderTexture.ReleaseTemporary(normalCache);
			RenderTexture.ReleaseTemporary(result);
		}
	}

	Vector3 RandomDir ()
	{
		//return RenderSettings.sun.transform.forward;
		float d, x, y, z;
		do {
			x = Random.value * 2.0f - 1.0f;
			y = Random.value * 2.0f - 1.0f;
			z = Random.value * 2.0f - 1.0f;
			d = x*x + y*y + z*z;
		} while(d > 1.0);
		return new Vector3(x, y, z);
	}

	Vector3[] PointsOnSphere (int n)
    {
        List<Vector3> upts = new List<Vector3>();
        float inc = Mathf.PI * (3 - Mathf.Sqrt(5));
        float off = 2.0f / n;
        float x = 0;
        float y = 0;
        float z = 0;
        float r = 0;
        float phi = 0;
       
        for (var k = 0; k < n; k++){
            y = k * off - 1 + (off /2);
            r = Mathf.Sqrt(1 - y * y);
            phi = k * inc;
            x = Mathf.Cos(phi) * r;
            z = Mathf.Sin(phi) * r;
           
            upts.Add(new Vector3(x, y, z));
        }
        Vector3[] pts = upts.ToArray();
        return pts;
    }

	void Bake (GameObject[] selection)
	{
		if (Application.isPlaying || selection == null || selection.Length < 1)
			return;

		Material dataCacher = new Material(Shader.Find("Hidden/BNAO_DataCacher"));
		Material postProcess = new Material(Shader.Find("Hidden/BNAO_PostProcess"));
		Material dilate = new Material(Shader.Find("Hidden/BNAO_Dilate"));
		Material bnao = new Material(Shader.Find("Hidden/BNAO"));
		Material composite = new Material(Shader.Find("Hidden/BNAO_Composite"));

		Shader depthShader;

		if (cullOverrideMode == CullOverrideMode.ForceTwoSided)
			depthShader = Shader.Find("Hidden/BNAO_Depth_2Sided");
		else if (cullOverrideMode == CullOverrideMode.ForceOneSided)
			depthShader = Shader.Find("Hidden/BNAO_Depth_1Sided");
		else
			depthShader = Shader.Find("Hidden/BNAO_Depth");

		// Get objects to be rendered
		var bnaoObjects = new Dictionary<Material, BNAOObject>();
		var meshRenderers        = new List<MeshRenderer>();
		var skinnedMeshRenderers = new List<SkinnedMeshRenderer>();
		foreach (var gameObject in selection)
		{
			meshRenderers.AddRange(gameObject.GetComponentsInChildren<MeshRenderer>());
			skinnedMeshRenderers.AddRange(gameObject.GetComponentsInChildren<SkinnedMeshRenderer>());
		}
		Material mat = null;
		foreach (var renderer in meshRenderers)
		{
			var meshFilter = renderer.GetComponent<MeshFilter>();
			if (meshFilter && meshFilter.sharedMesh)
			{
				if (forceSharedTexture)
				{
					if (bnaoObjects.Count < 1)
					{
						mat = renderer.sharedMaterial;
						bnaoObjects.Add(mat, new BNAOObject(renderer.name, (int)bakeRes));
					}
					bnaoObjects[mat].renderedMeshes.Add(new RendereredMesh(renderer, meshFilter.sharedMesh));
				}
				else
				{
					if (!bnaoObjects.ContainsKey(renderer.sharedMaterial))
						bnaoObjects.Add(renderer.sharedMaterial, new BNAOObject(renderer.name, (int)bakeRes));
					bnaoObjects[renderer.sharedMaterial].renderedMeshes.Add(new RendereredMesh(renderer, meshFilter.sharedMesh));
				}
			}	
		}
		foreach (var renderer in skinnedMeshRenderers)
		{
			if (renderer.sharedMesh)
			{
				Mesh mesh = new Mesh();
				mesh.name = renderer.sharedMesh.name + "_snapshot";
				renderer.BakeMesh(mesh);

				if (forceSharedTexture)
				{
					if (bnaoObjects.Count < 1)
					{
						mat = renderer.sharedMaterial;
						bnaoObjects.Add(mat, new BNAOObject(renderer.name, (int)bakeRes));
					}
					bnaoObjects[mat].renderedMeshes.Add(new RendereredMesh(renderer, mesh));
				}
				else
				{
					if (!bnaoObjects.ContainsKey(renderer.sharedMaterial))
						bnaoObjects.Add(renderer.sharedMaterial, new BNAOObject(renderer.name, (int)bakeRes));
					bnaoObjects[renderer.sharedMaterial].renderedMeshes.Add(new RendereredMesh(renderer, mesh));
				}
			}
		}

		if (bnaoObjects.Count < 1)
		{
			// No renderers found, abort
			return;
		}

		baking = true;

		// Bundle renderers which share materials

		// Mode switch
		switch (bakeMode)
		{
			case BakeMode.BentNormal:
				Shader.EnableKeyword("MODE_BN");
				Shader.DisableKeyword("MODE_AO");
			break;
			case BakeMode.AmbientOcclusion:
				Shader.DisableKeyword("MODE_BN");
				Shader.EnableKeyword("MODE_AO");
			break;
		}

		// Cache data to textures
		foreach (var bnaoObject in bnaoObjects)
		{
			Graphics.SetRenderTarget(new RenderBuffer[2] { bnaoObject.Value.positionCache.colorBuffer, bnaoObject.Value.normalCache.colorBuffer }, bnaoObject.Value.positionCache.depthBuffer);
			foreach (var renderMesh in bnaoObject.Value.renderedMeshes)
			{
				if (useNormalMaps && !forceSharedTexture)
				{
					if (renderMesh.renderer.sharedMaterial.HasProperty("_NormalTex"))
					{
						dataCacher.SetTexture("_NormalTex", renderMesh.renderer.sharedMaterial.GetTexture("_NormalTex"));
						dataCacher.SetFloat("_HasNormalTex", 1);
					}
					else if (renderMesh.renderer.sharedMaterial.HasProperty("_BumpMap"))
					{
						dataCacher.SetTexture("_NormalTex", renderMesh.renderer.sharedMaterial.GetTexture("_BumpMap"));
						dataCacher.SetFloat("_HasNormalTex", 1);
					}
					else
					{
						dataCacher.SetFloat("_HasNormalTex", 0);
					}
				}
				dataCacher.SetFloat("_UVChannel", (float)uvChannel);
				dataCacher.SetPass(0);
				Graphics.DrawMeshNow(renderMesh.mesh, renderMesh.renderer.transform.localToWorldMatrix);
			}
		}

		// Calculate spherical bounds of all objects to be rendered
		var bounds = new Bounds();
		bool first = true;
		foreach (var bnaoObject in bnaoObjects)
		{
			foreach (var rendermesh in bnaoObject.Value.renderedMeshes)
			{
				if (first)
				{
					bounds = rendermesh.renderer.bounds;
					first = false;
				}
				else
				{
					bounds.Encapsulate(rendermesh.renderer.bounds);
				}
			}
		}
		//var bounds = renderMeshes[0].renderer.bounds;
		//for (int i = 1; i < renderMeshes.Count; i++)
		//	bounds.Encapsulate(renderMeshes[i].renderer.bounds);
		Vector3 rendererCenter = bounds.center;
		float rendererRadius = Mathf.Max(Mathf.Max(bounds.extents.x, bounds.extents.y), bounds.extents.z);

		// Initialize shadow map
		var shadowMap = RenderTexture.GetTemporary((int)shadowMapRes, (int)shadowMapRes, 24, RenderTextureFormat.Shadowmap);

		// Initialize camera
		var go = new GameObject("BNAO_BakeCamera");
		var camera = go.AddComponent<Camera>();
		camera.enabled  = false;
		camera.farClipPlane = rendererRadius * 2;
		camera.nearClipPlane = 0.01f;
		camera.orthographic = true;
		camera.orthographicSize = rendererRadius;
		camera.aspect = 1f;
		camera.targetTexture = shadowMap;

		// Disable rest of scene
		var scene = FindObjectsOfType<Renderer>();
		var sceneEnabled = new bool[scene.Length];
		for (int i = 0; i < scene.Length; i++)
		{
			sceneEnabled[i] = scene[i].enabled;
			if (!includeScene)
				scene[i].enabled = false;
		}

		// Force enable bake objects and force double sided rendering
		var shadowCastingModes = new List<ShadowCastingMode>();
		foreach (var bnaoObject in bnaoObjects)
		{
			foreach (var renderMesh in bnaoObject.Value.renderedMeshes)
			{
				renderMesh.renderer.enabled = true;
				shadowCastingModes.Add(renderMesh.renderer.shadowCastingMode);
				renderMesh.renderer.shadowCastingMode = ShadowCastingMode.TwoSided;
			}
		}

		var directions = PointsOnSphere((int)samples);

		for (int sample = 0; sample < (int)samples; sample++)
		{
			EditorUtility.DisplayProgressBar("Baking " + fullName, "Baking sample " + sample + " / " + (int)(samples) + "...", (float)sample / (float)samples);
			
			// Get random vector
			//var dir = RandomDir();
			var dir = directions[sample];
			// Position the camera
			camera.transform.position = rendererCenter + dir * rendererRadius;
			// Aim the camera
			camera.transform.rotation = Quaternion.LookRotation(-dir);
			// Render the shadow map
			if (useOriginalShaders)
				camera.Render();
			else
				camera.RenderWithShader(depthShader, "RenderType");

			// Bind shadow map
			Shader.SetGlobalTexture("_ShadowMap", shadowMap);

			// Calculate world-to-shadow matrix
			var proj = camera.projectionMatrix;
			if (SystemInfo.usesReversedZBuffer) {
				proj.m20 = -proj.m20;
				proj.m21 = -proj.m21;
				proj.m22 = -proj.m22;
				proj.m23 = -proj.m23;
			}
			var view = camera.worldToCameraMatrix;
			var scaleOffset = Matrix4x4.identity;
			scaleOffset.m00 = scaleOffset.m11 = scaleOffset.m22 = 0.5f;
			scaleOffset.m03 = scaleOffset.m13 = scaleOffset.m23 = 0.5f;
			var worldToShadow = scaleOffset * (proj * view);
			Shader.SetGlobalMatrix("_WorldToShadow", worldToShadow);

			Shader.SetGlobalFloat("_Samples", (float)samples);
			Shader.SetGlobalFloat("_Sample", (float)sample);
			Shader.SetGlobalVector("_Dir", dir);
			Shader.SetGlobalFloat("_ShadowBias", shadowBias);
			Shader.SetGlobalFloat("_ClampToHemisphere", clampToHemisphere ? 1f : 0f);

			foreach (var bnaoObject in bnaoObjects)
			{
				bnao.SetTexture("_PositionCache", bnaoObject.Value.positionCache);
				bnao.SetTexture("_NormalCache", bnaoObject.Value.normalCache);
				var tmp = RenderTexture.GetTemporary(bnaoObject.Value.result.descriptor);
				tmp.filterMode = FilterMode.Point;
				Graphics.Blit(bnaoObject.Value.result, tmp);
				bnao.SetTexture("_PrevTex", tmp);
				Graphics.Blit(bnaoObject.Value.positionCache, bnaoObject.Value.result, bnao, 0);
				RenderTexture.ReleaseTemporary(tmp);
			}
		}

		EditorUtility.DisplayProgressBar("Baking " + fullName, "Post Processing...", 1);

		// Post process
		foreach (var bnaoObject in bnaoObjects)
		{
			var tmp = RenderTexture.GetTemporary(bnaoObject.Value.result.descriptor);
			tmp.filterMode = FilterMode.Point;
			Graphics.SetRenderTarget(tmp);
			postProcess.SetTexture("_MainTex", bnaoObject.Value.result);
			postProcess.SetFloat("_AOBias", aoBias);
			postProcess.SetFloat("_UVChannel", (float)uvChannel);
			postProcess.SetFloat("_NormalsSpace", (float)bentNormalsSpace);
			postProcess.SetPass(0);
			foreach (var renderMesh in bnaoObject.Value.renderedMeshes)
			{
				postProcess.SetMatrix("_WorldToObject", renderMesh.renderer.transform.worldToLocalMatrix);
				Graphics.DrawMeshNow(renderMesh.mesh, renderMesh.renderer.transform.localToWorldMatrix);
			}
			Graphics.Blit(tmp, bnaoObject.Value.result);
			RenderTexture.ReleaseTemporary(tmp);
		}

		// Dilate
		foreach (var bnaoObject in bnaoObjects)
		{
			var tmp = RenderTexture.GetTemporary(bnaoObject.Value.result.descriptor);
			tmp.filterMode = FilterMode.Point;
			for (int i = 0; i < dilation; i++)
			{
				EditorUtility.DisplayProgressBar("Baking", "Dilating...", (float)i / (float)dilation);
				dilate.SetTexture("_MainTex", bnaoObject.Value.result);
				Graphics.Blit(bnaoObject.Value.result, tmp, dilate);
				i++;
				if (i < dilation)
				{
					dilate.SetTexture("_MainTex", tmp);
					Graphics.Blit(tmp, bnaoObject.Value.result, dilate);
				}
				else
				{
					Graphics.Blit(tmp, bnaoObject.Value.result);
				}
			}
			RenderTexture.ReleaseTemporary(tmp);
		}

		// Output
		int u = 0;
		foreach (var bnaoObject in bnaoObjects)
		{
			EditorUtility.DisplayProgressBar("Baking " + fullName, "Saving texture(s)...", (float)u / bnaoObjects.Count);
			//RenderTextureToFile(bnaoObject.Value.result, "Assets/BNAO/Bakes/" + bnaoObject.Value.name + "_" + bakeMode.ToString() + ".png");
			var names = new List<string>();
			foreach (var renderedMesh in bnaoObject.Value.renderedMeshes)
				names.Add(renderedMesh.renderer.name);
			switch (nameMode)
			{
				case NameMode.Shortest:
					names = names.OrderBy(x => x.Length).ToList<string>();
				break;
				case NameMode.Longest:
					names = names.OrderByDescending(x => x.Length).ToList<string>();
				break;
				case NameMode.Alphabetical:
					names.Sort();
				break;
				case NameMode.Whatever:
				break;
			}
			RenderTextureToFile(bnaoObject.Value.result, outputPath + "/" + names[0] + "_" + bakeMode.ToString() + ".png", composite);
			u++;
		}

		// Restore shadow casting modes
		u = 0;
		foreach (var bnaoObject in bnaoObjects)
		{
			foreach (var renderMesh in bnaoObject.Value.renderedMeshes)
			{
				renderMesh.renderer.shadowCastingMode = shadowCastingModes[u];
				u++;
			}
		}
		//for (int i = 0; i < renderMeshes.Count; i++)
		//{
		//	renderMeshes[i].renderer.shadowCastingMode = shadowCastingModes[i];
		//}

		// Re-enable rest of scene
		for (int i = 0; i < scene.Length; i++)
			scene[i].enabled = sceneEnabled[i];

		// Clean up
		RenderTexture.ReleaseTemporary(shadowMap);
		DestroyImmediate(camera.gameObject);

		UnityEditor.AssetDatabase.Refresh();

		EditorUtility.ClearProgressBar();
		baking = false;
	}

	void RenderTextureToFile (RenderTexture rt, string path, Material composite)
	{
		var tmp = RenderTexture.GetTemporary(rt.descriptor);
		Graphics.SetRenderTarget(tmp);
		if (transparentPixels)
			GL.Clear(true, true, Color.clear);
		else
		{
			switch (bakeMode)
			{
				case BakeMode.BentNormal:
					GL.Clear(true, true, new Color(0.5f, 0.5f, 1f, 1f));
				break;
				case BakeMode.AmbientOcclusion:
					GL.Clear(true, true, new Color(1f, 1f, 1f, 1f));
				break;
			}
		}
		composite.SetTexture("_MainTex", rt);
		Graphics.Blit(rt, tmp, composite);

		RenderTexture.active = tmp;
        Texture2D tex = new Texture2D(tmp.width, tmp.height, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, tmp.width, tmp.height), 0, 0);
        RenderTexture.active = null;
		RenderTexture.ReleaseTemporary(tmp);

        byte[] bytes;
        bytes = tex.EncodeToPNG();
        
        System.IO.File.WriteAllBytes(path, bytes);
	}
}