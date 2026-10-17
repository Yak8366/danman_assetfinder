using System.Windows.Media;
using System.Windows.Media.Media3D;
using Assimp;

namespace DanmanAssetFinder.Services;

public static class MeshPreviewBuilder
{
    public static Model3DGroup? BuildFromFile(string meshPath)
    {
        if (!File.Exists(meshPath))
            return null;

        try
        {
            using var context = new AssimpContext();
            var scene = context.ImportFile(meshPath, PostProcessSteps.Triangulate | PostProcessSteps.JoinIdenticalVertices);
            if (scene?.Meshes is null || scene.Meshes.Count == 0)
                return null;

            var group = new Model3DGroup();
            foreach (var mesh in scene.Meshes)
            {
                var geometry = BuildGeometry(mesh);
                if (geometry is null)
                    continue;

                var color = Color.FromRgb(180, 200, 220);
                var material = new DiffuseMaterial(new SolidColorBrush(color));
                group.Children.Add(new GeometryModel3D(geometry, material) { BackMaterial = material });
            }

            return group.Children.Count > 0 ? group : null;
        }
        catch
        {
            return null;
        }
    }

    private static MeshGeometry3D? BuildGeometry(Mesh mesh)
    {
        if (mesh.VertexCount == 0 || mesh.FaceCount == 0)
            return null;

        var positions = new Point3DCollection(mesh.VertexCount);
        foreach (var v in mesh.Vertices)
            positions.Add(new Point3D(v.X, v.Y, v.Z));

        var indices = new Int32Collection();
        foreach (var face in mesh.Faces)
        {
            if (face.IndexCount != 3)
                continue;

            indices.Add(face.Indices[0]);
            indices.Add(face.Indices[1]);
            indices.Add(face.Indices[2]);
        }

        if (indices.Count == 0)
            return null;

        return new MeshGeometry3D
        {
            Positions = positions,
            TriangleIndices = indices
        };
    }
}
