using System.Numerics;
using GMLModel.Pmi;
using HT = HelixToolkit;

namespace GMLModel
{
    public class STEPModelLoader
    {
        /// <summary>
        /// Loads geometry plus PMI (dimensions/tolerances/notes/views) using the
        /// OCCT XDE/CAF pipeline.
        /// </summary>
        public PmiModel LoadStepToViewport(string path,
            out List<int> allIndices,
            out List<Vector3> allPositions,
            out HT.IntCollection lineIndices,
            out HT.Vector3Collection linePositions,
            bool readPmi)
        {
            if (readPmi)
            {
                return new StepPmiLoader().Load(path, out allIndices, out allPositions, out lineIndices, out linePositions);
            }
            LoadStepToViewport(path, out allIndices, out allPositions, out lineIndices, out linePositions);
            return new PmiModel();
        }

        public void LoadStepToViewport(string path,
            out List<int> allIndices, 
            out List<Vector3> allPositions, 
            out HT.IntCollection lineIndices, 
            out HT.Vector3Collection linePositions)
        {
            // Initialize OCCT native DLL paths
            OcctConfiguration.Configure();

            // Read the STEP file
            var reader = new Occt.STEPControl_Reader();
            var status = reader.ReadFile(path);
            if (status != Occt.IFSelect_ReturnStatus.IFSelect_RetDone)
                throw new InvalidOperationException($"Failed to read STEP file: {Path.GetFileName(path)} (status={status})");

            reader.TransferRoots();
            var shape = reader.OneShape();
            if (shape == null || shape.IsNull)
                throw new InvalidOperationException($"No geometry found in: {Path.GetFileName(path)}");

            // Tessellate the BRep shape into triangulated faces
            var mesher = new Occt.BRepMesh_IncrementalMesh(shape, 0.1, false, 0.5, true);

            // Collect positions and indices from all faces into a single Triangulation
            allPositions = new List<Vector3>();
            allIndices = new List<int>();

            var explorer = new Occt.TopExp_Explorer(shape, Occt.TopAbs_ShapeEnum.TopAbs_FACE);
            while (explorer.More)
            {
                var face = (Occt.TopoDS_Face)explorer.Current;
                var location = new Occt.TopLoc_Location();
                var poly = Occt.BRep_Tool.Triangulation(face, out location);
                if (poly != null)
                {
                    int vertexOffset = allPositions.Count;
                    var trsf = location.IsIdentity ? null : location.Transformation;

                    // Add positions
                    for (int i = 1; i <= poly.NbNodes; i++)
                    {
                        var pt = poly.Node(i);
                        if (trsf != null)
                            pt.Transform(trsf);
                        allPositions.Add(new Vector3((float)pt.X, (float)pt.Y, (float)pt.Z));
                    }

                    // Add triangle indices, respecting face orientation
                    bool reversed = face.Orientation == Occt.TopAbs_Orientation.TopAbs_REVERSED;
                    for (int i = 1; i <= poly.NbTriangles; i++)
                    {
                        var triangle = poly.Triangle(i);
                        int n1 = 0, n2 = 0, n3 = 0;
                        triangle.Get(out n1, out n2, out n3);
                        // OCCT uses 1-based indices
                        if (reversed)
                        {
                            allIndices.Add(vertexOffset + n1 - 1);
                            allIndices.Add(vertexOffset + n3 - 1);
                            allIndices.Add(vertexOffset + n2 - 1);
                        }
                        else
                        {
                            allIndices.Add(vertexOffset + n1 - 1);
                            allIndices.Add(vertexOffset + n2 - 1);
                            allIndices.Add(vertexOffset + n3 - 1);
                        }
                    }
                }
                explorer.Next();
            }

            if (allPositions.Count == 0)
                throw new InvalidOperationException($"No triangulated faces found in: {Path.GetFileName(path)}");

            

            // ===== Extract BRep edges and display as lines =====
            linePositions = new HT.Vector3Collection();
            lineIndices = new HT.IntCollection();

            var edgeExplorer = new Occt.TopExp_Explorer(shape, Occt.TopAbs_ShapeEnum.TopAbs_EDGE);
            while (edgeExplorer.More)
            {
                var edge = (Occt.TopoDS_Edge)edgeExplorer.Current;
                var edgeLoc = new Occt.TopLoc_Location();
                var polygon = Occt.BRep_Tool.Polygon3D(edge, out edgeLoc);
                if (polygon != null)
                {
                    var edgeTrsf = edgeLoc.IsIdentity ? null : edgeLoc.Transformation;
                    int nbNodes = polygon.NbNodes;
                    int baseIdx = linePositions.Count;
                    var nodes = polygon.Nodes;
                    for (int i = 1; i <= nbNodes; i++)
                    {
                        var pt = nodes[i];
                        if (edgeTrsf != null)
                            pt.Transform(edgeTrsf);
                        linePositions.Add(new Vector3((float)pt.X, (float)pt.Y, (float)pt.Z));
                    }
                    // Add line segment indices (pairs of consecutive vertices)
                    for (int i = 0; i < nbNodes - 1; i++)
                    {
                        lineIndices.Add(baseIdx + i);
                        lineIndices.Add(baseIdx + i + 1);
                    }
                }
                edgeExplorer.Next();
            }
        }
    }
}
