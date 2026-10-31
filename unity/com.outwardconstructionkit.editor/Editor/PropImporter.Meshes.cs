using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// Copying meshes from the rip, and cutting static-batched props out of the combined meshes they share.
internal static partial class PropImporter
{
    // Unity's standard shapes aren't files, so rips can't contain them. Every project already has them.
    private const string BUILTIN_RESOURCES = "0000000000000000e000000000000000";
    private static readonly Dictionary<long, string> s_builtinMeshes = new Dictionary<long, string>
    {
        { 10202, "Cube.fbx" },
        { 10206, "Cylinder.fbx" },
        { 10207, "Sphere.fbx" },
        { 10208, "Capsule.fbx" },
        { 10209, "Plane.fbx" },
        { 10210, "Quad.fbx" },
    };

    private static Mesh CopyMesh(string guid, string folder, long fileId = 0)
    {
        if (guid == BUILTIN_RESOURCES)
        {
            string builtin;
            return s_builtinMeshes.TryGetValue(fileId, out builtin) ? Resources.GetBuiltinResource<Mesh>(builtin) : null;
        }

        string file = RipFile(guid);
        return file == null ? null : AssetDatabase.LoadAssetAtPath<Mesh>(CopyWithMeta(file, folder + "/Meshes"));
    }

    private static Mesh CutFromCombined(RipScene.Node node, string folder)
    {
        string path = folder + "/Meshes/" + Safe(node.Name) + "_" + node.MeshGuid.Substring(0, 8) + "_" + node.FirstSubMesh + ".asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null)
            return existing;

        Mesh combined = LoadCombined(node.MeshGuid);
        if (combined == null)
            return null;

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var tangents = new List<Vector4>();
        var uv = new List<Vector2>();
        var colors = new List<Color32>();
        combined.GetVertices(vertices);
        combined.GetNormals(normals);
        combined.GetTangents(tangents);
        combined.GetUVs(0, uv);
        combined.GetColors(colors);

        // Combined vertices are already in world space, so they're carried back into the renderer's own.
        Matrix4x4 toLocal = node.WorldMatrix.inverse;
        Matrix4x4 normalMatrix = node.WorldMatrix.transpose;

        var map = new Dictionary<int, int>();
        var outVertices = new List<Vector3>();
        var outNormals = new List<Vector3>();
        var outTangents = new List<Vector4>();
        var outUv = new List<Vector2>();
        var outColors = new List<Color32>();
        var outIndices = new List<List<int>>();

        for (int s = node.FirstSubMesh; s < node.FirstSubMesh + node.SubMeshCount && s < combined.subMeshCount; s++)
        {
            var indices = new List<int>();
            foreach (int index in combined.GetIndices(s))
            {
                int mapped;
                if (!map.TryGetValue(index, out mapped))
                {
                    mapped = outVertices.Count;
                    map[index] = mapped;
                    outVertices.Add(toLocal.MultiplyPoint3x4(vertices[index]));
                    if (normals.Count > 0)
                        outNormals.Add(normalMatrix.MultiplyVector(normals[index]).normalized);
                    if (tangents.Count > 0)
                    {
                        Vector3 tangent = toLocal.MultiplyVector(tangents[index]).normalized;
                        outTangents.Add(new Vector4(tangent.x, tangent.y, tangent.z, tangents[index].w));
                    }
                    if (uv.Count > 0)
                        outUv.Add(uv[index]);
                    if (colors.Count > 0)
                        outColors.Add(colors[index]);
                }
                indices.Add(mapped);
            }
            outIndices.Add(indices);
        }

        var mesh = new Mesh { name = node.Name };
        if (outVertices.Count > 65535)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(outVertices);
        if (outNormals.Count > 0) mesh.SetNormals(outNormals);
        if (outTangents.Count > 0) mesh.SetTangents(outTangents);
        if (outUv.Count > 0) mesh.SetUVs(0, outUv);
        if (outColors.Count > 0) mesh.SetColors(outColors);
        mesh.subMeshCount = outIndices.Count;
        for (int i = 0; i < outIndices.Count; i++)
            mesh.SetIndices(outIndices[i], MeshTopology.Triangles, i);
        mesh.RecalculateBounds();

        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    // Ripped meshes are marked unreadable, as they were in the game, so the working copy clears that flag.
    private static Mesh LoadCombined(string guid)
    {
        string path = LIBRARY + "/_Combined/" + guid + ".asset";
        if (!File.Exists(path))
        {
            string file = RipFile(guid);
            if (file == null)
            {
                Debug.LogWarning("[OCK] Combined mesh not in the rip: " + guid);
                return null;
            }
            File.WriteAllText(path, File.ReadAllText(file).Replace("m_IsReadable: 0", "m_IsReadable: 1"));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        }
        return AssetDatabase.LoadAssetAtPath<Mesh>(path);
    }
}
