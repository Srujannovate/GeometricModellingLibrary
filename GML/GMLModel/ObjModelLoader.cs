using System.Globalization;
using System.Numerics;
using HT = HelixToolkit;

namespace GMLModel
{
    /// <summary>
    /// Loads the geometry subset of Wavefront OBJ without a native importer.
    /// This avoids Assimp native-runtime failures and supports the common OBJ
    /// forms: v, f, groups/objects, slash-separated face indices, and negative indices.
    /// </summary>
    public class ObjModelLoader
    {
        public void LoadObjToViewport(string path,
            out List<HT.Vector3Collection> positions,
            out List<HT.IntCollection> indices)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("OBJ file was not found.", path);

            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            int lineNumber = 0;
            foreach (var rawLine in File.ReadLines(path))
            {
                lineNumber++;
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                    continue;

                var comment = line.IndexOf('#');
                if (comment >= 0)
                    line = line[..comment].Trim();
                if (line.Length == 0)
                    continue;

                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                    continue;

                switch (parts[0])
                {
                    case "v":
                        if (parts.Length < 4 ||
                            !TryParseDouble(parts[1], out var x) ||
                            !TryParseDouble(parts[2], out var y) ||
                            !TryParseDouble(parts[3], out var z))
                        {
                            throw new FormatException($"Invalid vertex at OBJ line {lineNumber}.");
                        }
                        vertices.Add(new Vector3((float)x, (float)y, (float)z));
                        break;

                    case "f":
                        if (parts.Length < 4)
                            continue;

                        var face = new List<int>(parts.Length - 1);
                        for (int i = 1; i < parts.Length; i++)
                        {
                            var vertexToken = parts[i].Split('/')[0];
                            if (!int.TryParse(vertexToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
                                throw new FormatException($"Invalid face index at OBJ line {lineNumber}.");

                            var resolved = index > 0 ? index - 1 : vertices.Count + index;
                            if (resolved < 0 || resolved >= vertices.Count)
                                throw new FormatException($"Face index is out of range at OBJ line {lineNumber}.");
                            face.Add(resolved);
                        }

                        // OBJ permits polygons; triangulate them as a fan.
                        for (int i = 1; i + 1 < face.Count; i++)
                        {
                            triangles.Add(face[0]);
                            triangles.Add(face[i]);
                            triangles.Add(face[i + 1]);
                        }
                        break;
                }
            }

            if (vertices.Count == 0 || triangles.Count == 0)
                throw new InvalidOperationException($"No drawable vertices and faces found in: {Path.GetFileName(path)}");

            positions = new List<HT.Vector3Collection>
            {
                new HT.Vector3Collection(vertices.ToArray())
            };
            indices = new List<HT.IntCollection>
            {
                new HT.IntCollection(triangles.ToArray())
            };
        }

        private static bool TryParseDouble(string value, out double result)
            => double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture, out result);
    }
}