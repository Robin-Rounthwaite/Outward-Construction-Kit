using System;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;

// Builds the template bundle, whose type descriptions let the extractor carry the classes in the game's files.
// A class the extractor reports it can't describe gets added here. dev/make-template.ps1 runs this.
[InitializeOnLoad]
internal static class OckTemplateMaker
{
    private const string FOLDER = "Assets/_OckTemplateMaker";

    static OckTemplateMaker()
    {
        if (Environment.GetEnvironmentVariable("OCK_TEMPLATE_OUT") != null)
            EditorApplication.update += RunWhenIdle;
    }

    private static void RunWhenIdle()
    {
        if (EditorApplication.timeSinceStartup < 15 || EditorApplication.isCompiling || EditorApplication.isUpdating)
            return;
        EditorApplication.update -= RunWhenIdle;
        int code = 0;
        string output = Environment.GetEnvironmentVariable("OCK_TEMPLATE_OUT");
        try
        {
            var mesh = new Mesh { name = "TemplateMesh", vertices = new[] { Vector3.zero, Vector3.up, Vector3.right }, triangles = new[] { 0, 1, 2 } };
            AssetDatabase.CreateAsset(mesh, FOLDER + "/TemplateMesh.asset");
            var material = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(material, FOLDER + "/TemplateMaterial.mat");
            AssetDatabase.CreateAsset(new PhysicMaterial(), FOLDER + "/TemplatePhysics.physicMaterial");
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(FOLDER + "/TemplateController.controller");
            var clip = new AnimationClip();
            AssetDatabase.CreateAsset(clip, FOLDER + "/TemplateClip.anim");
            controller.AddMotion(clip);
            var legacyClip = new AnimationClip { legacy = true };
            AssetDatabase.CreateAsset(legacyClip, FOLDER + "/TemplateLegacyClip.anim");
            var billboard = new BillboardAsset { material = material };
            billboard.SetVertices(new[] { Vector2.zero, Vector2.up, Vector2.right });
            billboard.SetIndices(new ushort[] { 0, 1, 2 });
            billboard.SetImageTexCoords(new[] { new Vector4(0f, 0f, 1f, 1f) });
            AssetDatabase.CreateAsset(billboard, FOLDER + "/TemplateBillboard.asset");

            var root = new GameObject("Template");
            root.AddComponent<BoxCollider>();
            root.AddComponent<SphereCollider>();
            root.AddComponent<CapsuleCollider>();
            root.AddComponent<MeshCollider>();
            root.AddComponent<Rigidbody>();
            root.AddComponent<CharacterController>();
            root.AddComponent<NavMeshAgent>();
            root.AddComponent<NavMeshObstacle>();
            root.AddComponent<Animator>().runtimeAnimatorController = controller;
            root.AddComponent<AudioSource>();
            root.AddComponent<Light>();
            root.AddComponent<Projector>();
            root.AddComponent<CharacterJoint>();
            root.AddComponent<ReflectionProbe>();
            root.AddComponent<TemplateScript>();
            var legacy = root.AddComponent<Animation>();
            legacy.AddClip(legacyClip, "Legacy");

            // What a scene's camera, audio and weather rigs are made of.
            root.AddComponent<Camera>();
            root.AddComponent<FlareLayer>();
            root.AddComponent<AudioListener>();
            root.AddComponent<AudioReverbFilter>();
            root.AddComponent<AudioLowPassFilter>();
            root.AddComponent<AudioHighPassFilter>();
            root.AddComponent<AudioEchoFilter>();
            root.AddComponent<AudioChorusFilter>();
            root.AddComponent<AudioDistortionFilter>();
            root.AddComponent<AudioReverbZone>();
            root.AddComponent<WindZone>();
            root.AddComponent<LensFlare>();

            var child = new GameObject("Child");
            child.transform.SetParent(root.transform, false);
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            Renderer renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            root.AddComponent<LODGroup>().SetLODs(new[] { new LOD(0.1f, new[] { renderer }) });
            var skinned = new GameObject("Skinned").AddComponent<SkinnedMeshRenderer>();
            skinned.transform.SetParent(root.transform, false);
            var cloth = new GameObject("Cloth");
            cloth.transform.SetParent(root.transform, false);
            cloth.AddComponent<SkinnedMeshRenderer>();
            cloth.AddComponent<Cloth>();
            var particles = new GameObject("Particles").AddComponent<ParticleSystem>();
            particles.transform.SetParent(root.transform, false);
            var trail = new GameObject("Trail").AddComponent<TrailRenderer>();
            trail.transform.SetParent(root.transform, false);
            var line = new GameObject("Line").AddComponent<LineRenderer>();
            line.transform.SetParent(root.transform, false);
            var billboards = new GameObject("Billboard").AddComponent<BillboardRenderer>();
            billboards.billboard = billboard;
            billboards.transform.SetParent(root.transform, false);
            var tree = new GameObject("Tree").AddComponent<Tree>();
            tree.transform.SetParent(root.transform, false);
            PrefabUtility.SaveAsPrefabAsset(root, FOLDER + "/Template.prefab");
            UnityEngine.Object.DestroyImmediate(root);

            var build = new AssetBundleBuild { assetBundleName = "ock_template", assetNames = new[] { FOLDER + "/Template.prefab" } };
            if (BuildPipeline.BuildAssetBundles(output, new[] { build }, BuildAssetBundleOptions.UncompressedAssetBundle, BuildTarget.StandaloneWindows64) == null)
                code = 1;
        }
        catch (Exception e)
        {
            File.WriteAllText(Path.Combine(output, "failed.txt"), e.ToString());
            code = 1;
        }
        AssetDatabase.DeleteAsset(FOLDER);
        EditorApplication.Exit(code);
    }
}
