using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Everything the test needs, run from the command line (no clicking in the editor):
///   Unity -batchmode -quit -projectPath . -executeMethod Build.Setup
///   Unity -batchmode -quit -projectPath . -executeMethod Build.IOS
///   Unity -batchmode -quit -projectPath . -executeMethod Build.Mac
/// </summary>
public static class Build
{
    const string ScenePath = "Assets/Scenes/Battle.unity";
    const string Gen = "Assets/Resources/Generated";
    const string Mats = "Assets/Resources/Materials";

    public static void Setup()
    {
        Pipeline();
        Textures();
        GeneratedTextures();
        Materials();
        Units();
        Audio();
        Portraits();
        Scene();
        Player();
        AssetDatabase.SaveAssets();
        Debug.Log("SETUP DONE");
    }

    // ------------------------------------------------------------------ the realistic pipeline (URP): soft sun shadows, ambient occlusion, HDR

    static void Pipeline()
    {
        Directory.CreateDirectory("Assets/Settings");
        var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/URP.asset");
        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/URP_Renderer.asset");
        if (asset == null)
        {
            renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(renderer, "Assets/Settings/URP_Renderer.asset");
            asset = UniversalRenderPipelineAsset.Create(renderer);
            AssetDatabase.CreateAsset(asset, "Assets/Settings/URP.asset");
        }
        var so = new SerializedObject(asset);
        Set(so, "m_MainLightShadowmapResolution", 4096);
        Set(so, "m_ShadowDistance", 100f);
        Set(so, "m_ShadowCascadeCount", 1);
        Set(so, "m_SoftShadowsSupported", true);
        Set(so, "m_SoftShadowQuality", 3);
        Set(so, "m_SupportsHDR", true);
        Set(so, "m_MSAA", 4);
        Set(so, "m_RenderScale", 1f);
        Set(so, "m_AdditionalLightsRenderingMode", 1);
        Set(so, "m_AdditionalLightsPerObjectLimit", 4);
        Set(so, "m_SupportsCameraDepthTexture", true);
        Set(so, "m_MainLightShadowsSupported", true);
        Set(so, "m_ShadowDepthBias", 0.6f);
        Set(so, "m_ShadowNormalBias", 0.5f);
        so.ApplyModifiedPropertiesWithoutUndo();

        if (renderer != null)
        {
            var ro = new SerializedObject(renderer);
            Set(ro, "m_DepthPrimingMode", 0);
            ro.ApplyModifiedPropertiesWithoutUndo();
            AddAmbientOcclusion(renderer);
        }
        GraphicsSettings.defaultRenderPipeline = asset;
        for (var i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = asset;
        }
        PlayerSettings.colorSpace = ColorSpace.Linear;
        EditorUtility.SetDirty(asset);
    }

    static void AddAmbientOcclusion(UniversalRendererData renderer)
    {
        if (renderer.rendererFeatures.Any(f => f != null && f.GetType().Name == "ScreenSpaceAmbientOcclusion")) return;
        var type = typeof(UniversalRendererData).Assembly.GetType("UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion");
        if (type == null) { Debug.LogWarning("no SSAO feature type"); return; }
        var feature = (ScriptableRendererFeature)ScriptableObject.CreateInstance(type);
        feature.name = "ScreenSpaceAmbientOcclusion";
        AssetDatabase.AddObjectToAsset(feature, renderer);
        renderer.rendererFeatures.Add(feature);
        var so = new SerializedObject(renderer);
        var map = so.FindProperty("m_RendererFeatureMap");
        if (map != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long id))
        {
            map.arraySize++;
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = id;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        // stronger, tighter contact shadows than the default
        var fo = new SerializedObject(feature);
        Set(fo, "m_Settings.Intensity", 1.6f);
        Set(fo, "m_Settings.Radius", 0.4f);
        Set(fo, "m_Settings.DirectLightingStrength", 0.35f);
        Set(fo, "m_Settings.Downsample", true);
        fo.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(renderer);
        Debug.Log("SSAO added");
    }

    static void Set(SerializedObject so, string name, object value)
    {
        var p = so.FindProperty(name);
        if (p == null) { Debug.Log("no property " + name); return; }
        switch (value)
        {
            case int i: p.intValue = i; break;
            case float f: p.floatValue = f; break;
            case bool b: p.boolValue = b; break;
        }
    }

    // ------------------------------------------------------------------ pictures, ground, fonts

    static void Textures()
    {
        foreach (var path in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Resources/Buildings", "Assets/Resources/Ground" }).Select(AssetDatabase.GUIDToAssetPath))
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = TextureImporterType.Default;
            ti.mipmapEnabled = true;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.maxTextureSize = 2048;
            ti.anisoLevel = 4;
            if (path.Contains("/Ground/"))
            {
                ti.wrapMode = TextureWrapMode.Repeat;
            }
            else
            {
                ti.alphaIsTransparency = true;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.mipMapBias = -0.3f;
            }
            ti.SaveAndReimport();
        }
    }

    /// <summary>Soft puffs, rings, scorch marks, bark and stone, drawn here so nothing has to be downloaded.</summary>
    static void GeneratedTextures()
    {
        Directory.CreateDirectory(Gen);
        Write("puff", 128, 128, (x, y) =>
        {
            var d = Vector2.Distance(new Vector2(x, y), new Vector2(0.5f, 0.5f)) * 2;
            var n = Mathf.PerlinNoise(x * 6, y * 6) * 0.5f + Mathf.PerlinNoise(x * 13 + 5, y * 13 + 3) * 0.5f;
            var a = Mathf.Clamp01(1 - d) * Mathf.Clamp01(1 - d);
            a *= 0.55f + n * 0.7f;
            return new Color(1, 1, 1, Mathf.Clamp01(a * 1.3f));
        });
        Write("ring", 256, 256, (x, y) =>
        {
            var d = Vector2.Distance(new Vector2(x, y), new Vector2(0.5f, 0.5f)) * 2;
            var a = Mathf.Exp(-Mathf.Pow((d - 0.82f) / 0.07f, 2)) + Mathf.Clamp01(0.82f - d) * 0.18f;
            return new Color(1, 1, 1, Mathf.Clamp01(a) * (d < 1 ? 1 : 0));
        });
        Write("scorch", 256, 256, (x, y) =>
        {
            var d = Vector2.Distance(new Vector2(x, y), new Vector2(0.5f, 0.5f)) * 2;
            var n = Mathf.PerlinNoise(x * 5 + 1, y * 5 + 7);
            var a = Mathf.Clamp01((1 - d * (0.8f + n * 0.5f)) * 1.6f) * 0.85f;
            return new Color(0.08f, 0.06f, 0.05f, a);
        });
        Write("wood", 256, 512, (x, y) =>
        {
            var grain = Mathf.PerlinNoise(x * 40, y * 3) * 0.6f + Mathf.PerlinNoise(x * 90, y * 8) * 0.4f;
            var bark = Mathf.PerlinNoise(x * 18 + 3, y * 30) > 0.64f ? 0.72f : 1f;
            var v = (0.7f + grain * 0.4f) * bark;
            return new Color(0.66f * v, 0.53f * v, 0.38f * v, 1);
        });
        Write("stone", 256, 256, (x, y) =>
        {
            var n = Mathf.PerlinNoise(x * 12, y * 12) * 0.6f + Mathf.PerlinNoise(x * 40 + 9, y * 40 + 2) * 0.4f;
            var v = 0.7f + n * 0.45f;
            return new Color(0.5f * v, 0.47f * v, 0.42f * v, 1);
        });
        Write("macro", 256, 256, (x, y) =>
        {
            // large soft blotches of lusher and drier grass, so the ground never looks tiled
            var n = Mathf.PerlinNoise(x * 4 + 11, y * 4 + 3) * 0.6f + Mathf.PerlinNoise(x * 9 + 2, y * 9 + 8) * 0.3f + Mathf.PerlinNoise(x * 23, y * 23) * 0.1f;
            var v = 0.5f + (n - 0.5f) * 0.42f;
            return new Color(v * 1.02f, v, v * 0.94f, 1);
        });
        Write("dirt", 256, 256, (x, y) =>
        {
            var d = Vector2.Distance(new Vector2(x, y), new Vector2(0.5f, 0.5f)) * 2;
            var n = Mathf.PerlinNoise(x * 7 + 4, y * 7 + 1);
            var g = Mathf.PerlinNoise(x * 40, y * 40);
            var a = Mathf.Clamp01((1 - d) * 2.2f - 0.4f + (n - 0.5f) * 1.2f);
            var v = 0.8f + g * 0.35f;
            return new Color(0.56f * v, 0.46f * v, 0.33f * v, a * 0.85f);
        });
        AssetDatabase.Refresh();
        foreach (var name in new[] { "puff", "ring", "scorch", "wood", "stone", "macro", "dirt" })
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath($"{Gen}/{name}.png");
            ti.alphaIsTransparency = name is "puff" or "ring" or "scorch" or "dirt";
            ti.wrapMode = name is "wood" or "stone" or "macro" ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.SaveAndReimport();
        }
    }

    static void Write(string name, int w, int h, System.Func<float, float, Color> f)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var px = new Color[w * h];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
            px[y * w + x] = f((x + 0.5f) / w, (y + 0.5f) / h);
        tex.SetPixels(px);
        tex.Apply();
        File.WriteAllBytes($"{Gen}/{name}.png", tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }

    // ------------------------------------------------------------------ materials (kept as assets so the build keeps their shaders)

    const string Lit = "Universal Render Pipeline/Lit";
    const string Unlit = "Universal Render Pipeline/Unlit";
    const string PUnlit = "Universal Render Pipeline/Particles/Unlit";
    const string PLit = "Universal Render Pipeline/Particles/Lit";

    static Texture2D Tex(string path) => AssetDatabase.LoadAssetAtPath<Texture2D>(path);

    static void Materials()
    {
        Directory.CreateDirectory(Mats);
        // pictures are drawn early, back to front, before the depth blocks and the soldiers
        Mat("SpriteBase", Unlit, m => { Transparent(m, false); m.SetFloat("_Cull", 0); m.renderQueue = 1400; });
        // invisible blocks inside buildings and trees: they write depth only, so soldiers behind them are hidden
        Mat("Occluder", Unlit, m =>
        {
            m.SetFloat("_Surface", 0);
            m.SetFloat("_SrcBlend", (float)BlendMode.Zero);
            m.SetFloat("_DstBlend", (float)BlendMode.One);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.Zero);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_ZWrite", 1);
            m.SetShaderPassEnabled("ShadowCaster", false);
            m.SetShaderPassEnabled("DepthOnly", false);
            m.SetShaderPassEnabled("DepthNormals", false);
            m.renderQueue = 1700;
        });
        Mat("Ground", Lit, m =>
        {
            m.SetTexture("_BaseMap", Tex("Assets/Resources/Ground/ground_plain.png"));
            m.SetTextureScale("_BaseMap", new Vector2(36, 36));
            m.SetColor("_BaseColor", new Color(0.86f, 0.86f, 0.84f));
            m.SetFloat("_Smoothness", 0.08f);
            m.SetFloat("_Metallic", 0);
            m.SetFloat("_ZWrite", 0);
            m.SetFloat("_EnvironmentReflections", 0);
            m.SetFloat("_SpecularHighlights", 0);
            m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            m.SetTexture("_DetailAlbedoMap", Tex($"{Gen}/macro.png"));
            m.SetTextureScale("_DetailAlbedoMap", new Vector2(3, 3));
            m.SetFloat("_DetailAlbedoMapScale", 1f);
            m.EnableKeyword("_DETAIL_MULX2");
            m.renderQueue = 1000;
        });
        Mat("Dirt", Lit, m =>
        {
            m.SetTexture("_BaseMap", Tex($"{Gen}/dirt.png"));
            m.SetFloat("_Smoothness", 0.05f);
            Transparent(m, false);
            m.SetFloat("_EnvironmentReflections", 0);
            m.SetFloat("_SpecularHighlights", 0);
            m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            // drawn with the ground, before soldiers and pictures
            m.renderQueue = 1001;
        });
        Mat("Wood", Lit, m =>
        {
            m.SetTexture("_BaseMap", Tex($"{Gen}/wood.png"));
            m.SetTextureScale("_BaseMap", new Vector2(1, 1));
            m.SetColor("_BaseColor", new Color(0.95f, 0.9f, 0.85f));
            m.SetFloat("_Smoothness", 0.18f);
            m.SetFloat("_Cull", 0);
        });
        Mat("Charred", Lit, m =>
        {
            m.SetTexture("_BaseMap", Tex($"{Gen}/wood.png"));
            m.SetColor("_BaseColor", new Color(0.3f, 0.24f, 0.2f));
            m.SetFloat("_Smoothness", 0.1f);
        });
        Mat("Stone", Lit, m =>
        {
            m.SetTexture("_BaseMap", Tex($"{Gen}/stone.png"));
            m.SetColor("_BaseColor", new Color(0.95f, 0.92f, 0.88f));
            m.SetFloat("_Smoothness", 0.12f);
        });
        Mat("Shaft", Lit, m => { m.SetColor("_BaseColor", new Color(0.5f, 0.36f, 0.22f)); m.SetFloat("_Smoothness", 0.3f); });
        Mat("Steel", Lit, m => { m.SetColor("_BaseColor", new Color(0.7f, 0.72f, 0.75f)); m.SetFloat("_Metallic", 0.9f); m.SetFloat("_Smoothness", 0.65f); });
        Mat("Feather", Lit, m => { m.SetColor("_BaseColor", new Color(0.92f, 0.9f, 0.84f)); m.SetFloat("_Cull", 0); });
        Mat("Dust", PUnlit, m => { m.SetTexture("_BaseMap", Tex($"{Gen}/puff.png")); Transparent(m, false); });
        Mat("Smoke", PUnlit, m => { m.SetTexture("_BaseMap", Tex($"{Gen}/puff.png")); Transparent(m, false); });
        Mat("Fire", PUnlit, m => { m.SetTexture("_BaseMap", Tex($"{Gen}/puff.png")); Transparent(m, true); m.SetColor("_BaseColor", new Color(1.6f, 1.3f, 1f, 1)); });
        Mat("Debris", PLit, m => { m.SetColor("_BaseColor", Color.white); m.SetFloat("_Smoothness", 0.1f); });
        Mat("Ring", Unlit, m => { m.SetTexture("_BaseMap", Tex($"{Gen}/ring.png")); Transparent(m, false); m.renderQueue = 2990; });
        Mat("HeroRing", Unlit, m =>
        {
            m.SetTexture("_BaseMap", Tex($"{Gen}/ring.png"));
            Transparent(m, false);
            m.SetColor("_BaseColor", new Color(1f, 0.82f, 0.35f, 0.75f));
            m.renderQueue = 2990;
        });
        Mat("Scorch", Unlit, m => { m.SetTexture("_BaseMap", Tex($"{Gen}/scorch.png")); Transparent(m, false); m.renderQueue = 2980; });
        Mat("Line", PUnlit, m => { Transparent(m, false); m.renderQueue = 2995; });
    }

    static Material Mat(string name, string shader, System.Action<Material> set)
    {
        var path = $"{Mats}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        var sh = Shader.Find(shader);
        if (sh == null) Debug.LogError("no shader " + shader);
        if (m == null)
        {
            m = new Material(sh);
            AssetDatabase.CreateAsset(m, path);
        }
        else
        {
            m.shader = sh;
        }
        set(m);
        EditorUtility.SetDirty(m);
        return m;
    }

    static void Transparent(Material m, bool additive)
    {
        m.SetFloat("_Surface", 1);
        m.SetFloat("_Blend", additive ? 2 : 0);
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        m.SetFloat("_DstBlendAlpha", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetShaderPassEnabled("ShadowCaster", false);
        m.SetShaderPassEnabled("DepthOnly", false);
        m.renderQueue = (int)RenderQueue.Transparent;
    }

    // ------------------------------------------------------------------ the motion captured soldiers

    static void Units()
    {
        foreach (var dir in Directory.GetDirectories("Assets/Resources/Units"))
        {
            var folder = Path.GetFileName(dir);
            foreach (var path in Directory.GetFiles(dir, "*.fbx"))
            {
                var file = Path.GetFileNameWithoutExtension(path);
                var move = file.Substring(folder.Length + 1);
                var skin = move == "idle";
                var mi = (ModelImporter)AssetImporter.GetAtPath(path);
                mi.importBlendShapes = false;
                mi.importVisibility = false;
                mi.importCameras = false;
                mi.importLights = false;
                mi.animationType = ModelImporterAnimationType.Legacy;
                mi.importAnimation = true;
                mi.animationCompression = ModelImporterAnimationCompression.KeyframeReduction;
                mi.isReadable = skin && folder == "paladin";
                mi.materialImportMode = skin ? ModelImporterMaterialImportMode.ImportStandard : ModelImporterMaterialImportMode.None;
                var clips = mi.defaultClipAnimations;
                foreach (var c in clips)
                {
                    c.name = move;
                    c.loopTime = move is "idle" or "walk" or "run" or "cheer" or "aim";
                    c.wrapMode = c.loopTime ? WrapMode.Loop : WrapMode.ClampForever;
                }
                mi.clipAnimations = clips;
                mi.SaveAndReimport();
                if (skin) Skin(mi, path, dir);
            }
        }
    }

    /// <summary>Pulls the textures out of the model and gives each of its materials a proper URP one.</summary>
    static void Skin(ModelImporter mi, string path, string dir)
    {
        var texDir = $"{dir}/Textures";
        if (!Directory.Exists(texDir) || Directory.GetFiles(texDir).Length == 0)
        {
            Directory.CreateDirectory(texDir);
            mi.ExtractTextures(texDir);
            AssetDatabase.Refresh();
        }
        var textures = Directory.GetFiles(texDir).Where(f => !f.EndsWith(".meta")).ToList();
        foreach (var t in textures)
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(t);
            if (ti == null) continue;
            var lower = Path.GetFileNameWithoutExtension(t).ToLowerInvariant();
            var normal = lower.Contains("normal") || lower.EndsWith("_nm") || lower.Contains("stdnm");
            ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.sRGBTexture = !(normal || lower.Contains("specular"));
            ti.maxTextureSize = 2048;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.alphaIsTransparency = lower.Contains("eyelash");
            ti.SaveAndReimport();
        }
        // start from the model's own materials every time, so their textures can be read
        foreach (var key in mi.GetExternalObjectMap().Keys.ToList()) mi.RemoveRemap(key);
        mi.SaveAndReimport();

        var matDir = $"{dir}/Materials";
        Directory.CreateDirectory(matDir);
        var names = new HashSet<string>();
        foreach (var m in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>()) names.Add(m.name);
        foreach (var key in mi.GetExternalObjectMap().Keys.Where(k => k.type == typeof(Material))) names.Add(key.name);
        var embedded = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().GroupBy(m => m.name).ToDictionary(g => g.Key, g => g.First());
        foreach (var name in names)
        {
            var matPath = $"{matDir}/{name}.mat";
            // on later runs the model already uses our material: read the textures back from it
            if (!embedded.TryGetValue(name, out var src)) src = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            var albedo = Pick(src, textures, name, "_BaseMap", "_MainTex", "diffuse", "diff");
            var bump = Pick(src, textures, name, "_BumpMap", null, "normal", "nm");
            var spec = albedo != null ? Sibling(textures, AssetDatabase.GetAssetPath(albedo), "specular") : null;
            var m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (m == null)
            {
                m = new Material(Shader.Find(Lit));
                AssetDatabase.CreateAsset(m, matPath);
            }
            m.shader = Shader.Find(Lit);
            m.SetTexture("_BaseMap", albedo);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", 0.42f);
            if (bump != null)
            {
                m.SetTexture("_BumpMap", bump);
                m.SetFloat("_BumpScale", 1f);
                m.EnableKeyword("_NORMALMAP");
            }
            if (spec != null)
            {
                m.SetFloat("_WorkflowMode", 0);
                m.EnableKeyword("_SPECULAR_SETUP");
                m.SetTexture("_SpecGlossMap", spec);
                m.EnableKeyword("_METALLICSPECGLOSSMAP");
                m.SetColor("_SpecColor", Color.white);
                m.SetFloat("_Smoothness", 0.55f);
            }
            else
            {
                m.SetFloat("_Metallic", 0f);
                m.SetFloat("_Smoothness", name.ToLowerInvariant().Contains("bow") ? 0.35f : 0.3f);
            }
            var lname = name.ToLowerInvariant();
            var aname = albedo != null ? albedo.name.ToLowerInvariant() : "";
            if (aname.Contains("eyelash") || lname.Contains("phong") || lname.Contains("hair"))
            {
                m.SetFloat("_AlphaClip", 1);
                m.SetFloat("_Cutoff", 0.45f);
                m.EnableKeyword("_ALPHATEST_ON");
                m.SetFloat("_Cull", 0);
                m.renderQueue = (int)RenderQueue.AlphaTest;
            }
            if (lname.Contains("eyespec"))
            {
                Transparent(m, false);
                m.SetColor("_BaseColor", new Color(1, 1, 1, 0.06f));
            }
            EditorUtility.SetDirty(m);
            mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name), m);
            Debug.Log($"material {name}: albedo {albedo?.name} normal {bump?.name} spec {spec?.name}");
        }
        mi.SaveAndReimport();
    }

    static Texture2D Pick(Material src, List<string> textures, string matName, string prop, string prop2, string word, string word2)
    {
        if (src != null)
        {
            if (src.HasProperty(prop) && src.GetTexture(prop) is Texture2D a) return a;
            if (prop2 != null && src.HasProperty(prop2) && src.GetTexture(prop2) is Texture2D b) return b;
        }
        var stem = matName.Split('_')[0].ToLowerInvariant();
        var hits = textures.Where(t =>
        {
            var n = Path.GetFileNameWithoutExtension(t).ToLowerInvariant();
            return n.Contains(word) || n.EndsWith("_" + word2);
        }).ToList();
        var best = hits.FirstOrDefault(t => Path.GetFileNameWithoutExtension(t).ToLowerInvariant().Contains(stem)) ?? (hits.Count == 1 ? hits[0] : null);
        return best != null ? Tex(best) : null;
    }

    static Texture2D Sibling(List<string> textures, string albedoPath, string word)
    {
        var stem = Path.GetFileNameWithoutExtension(albedoPath).ToLowerInvariant().Replace("diffuse", "").Replace("diff", "");
        var hit = textures.FirstOrDefault(t =>
        {
            var n = Path.GetFileNameWithoutExtension(t).ToLowerInvariant();
            return n.Contains(word) && n.Replace(word, "") == stem;
        });
        return hit != null ? Tex(hit) : null;
    }

    /// <summary>Card pictures of each soldier, rendered here once with the game's lighting.</summary>
    /// <summary>Card pictures of each soldier (rendered in Blender).</summary>
    static void Portraits()
    {
        const string dir = "Assets/Resources/Portraits";
        if (!Directory.Exists(dir)) return;
        AssetDatabase.Refresh();
        foreach (var path in Directory.GetFiles(dir, "*.png"))
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.mipmapEnabled = false;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.SaveAndReimport();
        }
    }

    static void Audio()
    {
        foreach (var path in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Resources/Audio" }).Select(AssetDatabase.GUIDToAssetPath))
        {
            var ai = (AudioImporter)AssetImporter.GetAtPath(path);
            var s = ai.defaultSampleSettings;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.7f;
            s.loadType = path.Contains("battle") ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            ai.defaultSampleSettings = s;
            ai.SaveAndReimport();
        }
    }

    // ------------------------------------------------------------------ the one scene and the player settings

    static void Scene()
    {
        Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("Battle").AddComponent<BattleBootstrap>();
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
    }

    static void Player()
    {
        PlayerSettings.companyName = "Wahj Digital";
        PlayerSettings.productName = "Fight Test";
        PlayerSettings.bundleVersion = "0.1";
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, "com.wahjdigital.fighttest");
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "com.wahjdigital.fighttest");
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;
        PlayerSettings.allowedAutorotateToLandscapeRight = true;
        PlayerSettings.allowedAutorotateToPortrait = false;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.iOS.appleDeveloperTeamID = "DWCTY4YWZZ";
        PlayerSettings.iOS.appleEnableAutomaticSigning = true;
        PlayerSettings.iOS.targetOSVersionString = "16.0";
        PlayerSettings.iOS.hideHomeButton = true;
        PlayerSettings.statusBarHidden = true;
        PlayerSettings.SplashScreen.show = false;
        PlayerSettings.defaultScreenWidth = 1600;
        PlayerSettings.defaultScreenHeight = 740;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.resizableWindow = true;
        PlayerSettings.runInBackground = true;
        PlayerSettings.enableFrameTimingStats = true;
        PlayerSettings.visibleInBackground = true;
        PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.iOS, ManagedStrippingLevel.Minimal);
        // skin the soldiers on the graphics chip, in batches
        var pso = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").FirstOrDefault());
        var md = pso.FindProperty("meshDeformation");
        if (md != null) { md.intValue = 2; pso.ApplyModifiedPropertiesWithoutUndo(); }
        var gs = pso.FindProperty("gpuSkinning");
        if (gs != null) { gs.boolValue = true; pso.ApplyModifiedPropertiesWithoutUndo(); }
        // the old input manager (touches and mouse through UnityEngine.Input)
        var ps = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").FirstOrDefault();
        if (ps != null)
        {
            var so = new SerializedObject(ps);
            var p = so.FindProperty("activeInputHandler");
            if (p != null) { p.intValue = 0; so.ApplyModifiedPropertiesWithoutUndo(); }
        }
    }

    // ------------------------------------------------------------------ builds

    public static void IOS()
    {
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = "Builds/iOS",
            target = BuildTarget.iOS,
            options = BuildOptions.None,
        });
        Debug.Log("IOS BUILD " + report.summary.result);
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
    }

    public static void Mac()
    {
        var report = BuildPipeline.BuildPlayer(new[] { ScenePath }, "Builds/Mac/BattleTest.app", BuildTarget.StandaloneOSX, BuildOptions.Development);
        Debug.Log("MAC BUILD " + report.summary.result);
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
    }
}
