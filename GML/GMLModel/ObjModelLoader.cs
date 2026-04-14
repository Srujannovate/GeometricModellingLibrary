using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Assimp;
using HT = HelixToolkit;

namespace GMLModel
{
    public class ObjModelLoader
    {
        public ObjModelLoader() { }

        public void LoadObjToViewport(string path,
            out List<HT.Vector3Collection> positions,
            out List<HT.IntCollection> indices) 
        {
            var context = new AssimpContext();
            var scene = context.ImportFile(path, PostProcessSteps.Triangulate | PostProcessSteps.JoinIdenticalVertices);
            if (scene == null || !scene.HasMeshes)
                throw new InvalidOperationException($"No meshes found in: {Path.GetFileName(path)}");
            positions = new List<HT.Vector3Collection>();
            indices = new List<HT.IntCollection>();

            foreach (var mesh in scene.Meshes)
            {
                
                var posList = new List<Vector3>(mesh.VertexCount);
                for (int i = 0; i < mesh.VertexCount; i++)
                {
                    var v = mesh.Vertices[i];
                    posList.Add(new Vector3(v.X, v.Y, v.Z));
                }

                var idx = new List<int>(mesh.FaceCount * 3);
                foreach (var f in mesh.Faces)
                {
                    if (f.IndexCount == 3)
                    {
                        idx.Add(f.Indices[0]);
                        idx.Add(f.Indices[1]);
                        idx.Add(f.Indices[2]);
                    }
                }
                
                // Build Helix geometry for rendering from the triangulation
                positions.Add(new HT.Vector3Collection(posList.ToArray()));
                indices.Add(new HT.IntCollection(idx.ToArray()));
            }
        }
    }
}
