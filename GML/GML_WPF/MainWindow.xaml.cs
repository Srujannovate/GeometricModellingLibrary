using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Hx = HelixToolkit.Wpf.SharpDX;
using HxCore = HelixToolkit.SharpDX.Core;
using HxSharpDX = HelixToolkit.SharpDX;
using HxGeom = HelixToolkit.Geometry;
using System.Numerics;
using System.Collections.Generic;
using HT = HelixToolkit;
using System.Windows.Input;
using Microsoft.Win32;
using Assimp;
using VB = Microsoft.VisualBasic;
using SDX = SharpDX;

namespace GML_WPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private sealed class Triangulation
        {
            public Vector3[] Positions = Array.Empty<Vector3>();
            public int[] Indices = Array.Empty<int>();
            public Transform3DGroup Transform = new Transform3DGroup();
            public Hx.MeshGeometryModel3D? Model; // visualization hook
            // Cached local-space AABB (min/max)
            public Vector3 LocalMin;
            public Vector3 LocalMax;
            // Uniform grid acceleration
            public (int X, int Y, int Z) GridDims;
            public Vector3 GridOrigin; // usually LocalMin
            public Vector3 CellSize;   // per-axis cell size
            public Dictionary<(int x,int y,int z), List<int>> Grid = new(); // maps cell -> list of triangle base indices (i of Indices[i..i+2])
        }

        private readonly Hx.PerspectiveCamera _camera = new Hx.PerspectiveCamera
        {
            Position = new Point3D(0, 0, 8),
            LookDirection = new System.Windows.Media.Media3D.Vector3D(0, 0, -8),
            UpDirection = new System.Windows.Media.Media3D.Vector3D(0, 1, 0),
            FieldOfView = 45
        };

        // Keep models as list of triangulations
        private readonly List<Triangulation> _triangulations = new();
        // KD-tree cache over ALL loaded models (combined)
        private IntPtr _kdTree = IntPtr.Zero;
        private int _kdPointCount = 0;
        private bool _kdDirty = true;

        public MainWindow()
        {
            InitializeComponent();
            this.Closed += (_, __) => { if (_kdTree != IntPtr.Zero) { GML.Interop.Gml.KDTreeDestroy(_kdTree); _kdTree = IntPtr.Zero; } };

            // Initialize DirectX manager and camera in code-behind
            View3D.EffectsManager = new HxSharpDX.DefaultEffectsManager();
            View3D.Camera = _camera;

            // Bind gestures explicitly: left=rotate, right=pan
            View3D.InputBindings.Add(new MouseBinding(Hx.ViewportCommands.Rotate, new MouseGesture(MouseAction.LeftClick)));
            View3D.InputBindings.Add(new MouseBinding(Hx.ViewportCommands.Pan, new MouseGesture(MouseAction.RightClick)));

            // Add a cube to the scene by constructing geometry directly
            var positions = new HT.Vector3Collection
            {
                new Vector3(-1,-1,-1), new Vector3(1,-1,-1), new Vector3(1,1,-1), new Vector3(-1,1,-1),
                new Vector3(-1,-1, 1), new Vector3(1,-1, 1), new Vector3(1,1, 1), new Vector3(-1,1, 1)
            };
            var indices = new HT.IntCollection
            {
                // front (-Z)
                0,1,2, 0,2,3,
                // right (+X)
                1,5,6, 1,6,2,
                // back (+Z)
                5,4,7, 5,7,6,
                // left (-X)
                4,0,3, 4,3,7,
                // top (+Y)
                3,2,6, 3,6,7,
                // bottom (-Y)
                4,5,1, 4,1,0
            };

            var cubeMesh = new HxSharpDX.MeshGeometry3D
            {
                Positions = positions,
                Indices = indices
            };

            var cubeModel = new Hx.MeshGeometryModel3D
            {
                Geometry = cubeMesh,
                Material = Hx.PhongMaterials.Orange,
                RenderWireframe = true
            };

            //View3D.Items.Add(cubeModel);

            // Optional: handle menu actions like reset camera
            ResetCameraMenuItem.Click += (_, __) =>
            {
                _camera.Position = new Point3D(0, 0, 8);
                _camera.LookDirection = new System.Windows.Media.Media3D.Vector3D(0, 0, -8);
                _camera.UpDirection = new System.Windows.Media.Media3D.Vector3D(0, 1, 0);
            };
        }

        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OpenMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Open 3D Model",
                Filter = "Wavefront OBJ (*.obj)|*.obj|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog(this) == true)
            {
                try
                {
                    LoadObjToViewport(dlg.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Failed to load OBJ", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void NearestPointMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var text = VB.Interaction.InputBox("qx,qy,qz (world)", "Nearest Point", "0,0,0", -1, -1);
            if (string.IsNullOrWhiteSpace(text)) return;
            if (!TryParse3(text, out var qx, out var qy, out var qz))
            {
                MessageBox.Show(this, "Enter three comma-separated numbers, e.g. 0,0,0", "Invalid input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var tree = EnsureNativeTree();
            if (tree == IntPtr.Zero) { MessageBox.Show(this, "No points loaded."); return; }
            if (GML.Interop.Gml.KDTreeNearest(tree, qx, qy, qz, out var nx, out var ny, out var nz, out var dist))
            {
                ShowPointOverlay(nx, ny, nz);
                MessageBox.Show(this, $"Nearest: ({nx:F6}, {ny:F6}, {nz:F6})\nDistance: {dist:F6}", "KD-Tree Nearest");
            }
            else
            {
                MessageBox.Show(this, "No nearest point found (tree empty).", "KD-Tree Nearest");
            }
        }

        private void LoadObjToViewport(string path)
        {
            // Remove previously imported triangulations from viewport (keep the sample cube)
            foreach (var t in _triangulations)
            {
                if (t.Model != null) View3D.Items.Remove(t.Model);
            }
            _triangulations.Clear();

            var context = new AssimpContext();
            var scene = context.ImportFile(path, PostProcessSteps.Triangulate | PostProcessSteps.JoinIdenticalVertices);
            if (scene == null || !scene.HasMeshes)
                throw new InvalidOperationException($"No meshes found in: {Path.GetFileName(path)}");

            foreach (var mesh in scene.Meshes)
            {
                // Keep a triangulation copy
                var tri = new Triangulation();
                var posList = new List<Vector3>(mesh.VertexCount);
                for (int i = 0; i < mesh.VertexCount; i++)
                {
                    var v = mesh.Vertices[i];
                    posList.Add(new Vector3(v.X, v.Y, v.Z));
                }
                tri.Positions = posList.ToArray();
                // Compute local-space AABB
                ComputeLocalAabb(tri.Positions, out tri.LocalMin, out tri.LocalMax);
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
                tri.Indices = idx.ToArray();

                // Build uniform grid accelerator
                BuildGrid(tri);

                // Build Helix geometry for rendering from the triangulation
                var positions = new HT.Vector3Collection(tri.Positions);
                var indices = new HT.IntCollection(tri.Indices);
                var meshGeom = new HxSharpDX.MeshGeometry3D
                {
                    Positions = positions,
                    Indices = indices
                };
                var model = new Hx.MeshGeometryModel3D
                {
                    Geometry = meshGeom,
                    Material = Hx.PhongMaterials.Gray,
                    RenderWireframe = true,
                    WireframeColor = Colors.Lime
                };
                // Share the Transform3DGroup instance with the triangulation
                model.Transform = tri.Transform;
                tri.Model = model;

                View3D.Items.Add(model);
                _triangulations.Add(tri);
            }
            MarkKdDirty();
        }

        // ===== Transform menu handlers =====
        private void ResetModelTransformMenuItem_Click(object sender, RoutedEventArgs e)
        {
            foreach (var t in _triangulations)
            {
                t.Transform = new Transform3DGroup();
                if (t.Model != null) t.Model.Transform = t.Transform;
            }
            MarkKdDirty();
        }

        private void TranslateMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var text = VB.Interaction.InputBox("dx,dy,dz", "Translate", "0,0,0", -1, -1);
            if (string.IsNullOrWhiteSpace(text)) return;
            if (!TryParse3(text, out var a, out var b, out var c))
            {
                MessageBox.Show(this, "Enter three comma-separated numbers, e.g. 10,0,-5", "Invalid input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            foreach (var t in _triangulations)
            {
                t.Transform.Children.Add(new TranslateTransform3D(a, b, c));
            }
            MarkKdDirty();
        }

        private void RotateMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var text = VB.Interaction.InputBox("angle_deg,axisX,axisY,axisZ", "Rotate", "45,0,1,0", -1, -1);
            if (string.IsNullOrWhiteSpace(text)) return;
            var parts = text.Split(',');
            if (parts.Length != 4 || !double.TryParse(parts[0], out var ang) ||
                !double.TryParse(parts[1], out var ax) || !double.TryParse(parts[2], out var ay) || !double.TryParse(parts[3], out var az))
            {
                MessageBox.Show(this, "Enter angle and axis as: 45,0,1,0", "Invalid input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var len = Math.Sqrt(ax * ax + ay * ay + az * az);
            if (len < 1e-9) { MessageBox.Show(this, "Axis must be non-zero", "Invalid input"); return; }
            ax /= len; ay /= len; az /= len;
            foreach (var t in _triangulations)
            {
                var rot = new AxisAngleRotation3D(new System.Windows.Media.Media3D.Vector3D(ax, ay, az), ang);
                t.Transform.Children.Add(new RotateTransform3D(rot));
            }
            MarkKdDirty();
        }

        private void ScaleMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var text = VB.Interaction.InputBox("sx,sy,sz", "Scale", "1,1,1", -1, -1);
            if (string.IsNullOrWhiteSpace(text)) return;
            if (!TryParse3(text, out var sx, out var sy, out var sz))
            {
                MessageBox.Show(this, "Enter three comma-separated numbers, e.g. 1,1,1", "Invalid input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            foreach (var t in _triangulations)
            {
                t.Transform.Children.Add(new ScaleTransform3D(sx, sy, sz));
            }
            MarkKdDirty();
        }

        private static bool TryParse3(string text, out double a, out double b, out double c)
        {
            a = b = c = 0;
            var p = text.Split(',');
            return p.Length == 3 && double.TryParse(p[0], out a) && double.TryParse(p[1], out b) && double.TryParse(p[2], out c);
        }

        // ===== KD-Tree menu handlers (now calling native GML_Library) =====
        private void SphereIntersectMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var text = VB.Interaction.InputBox("cx,cy,cz,r", "Sphere Intersection", "0,0,0,1", -1, -1);
            if (string.IsNullOrWhiteSpace(text)) return;
            var p = text.Split(',');
            if (p.Length != 4 || !double.TryParse(p[0], out var cx) || !double.TryParse(p[1], out var cy) ||
                !double.TryParse(p[2], out var cz) || !double.TryParse(p[3], out var r))
            {
                MessageBox.Show(this, "Enter: cx,cy,cz,r", "Invalid input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var tree = EnsureNativeTree();
            if (tree == IntPtr.Zero) { MessageBox.Show(this, "No points to test."); return; }
            bool hit = GML.Interop.Gml.KDTreeSphereIntersects(tree, cx, cy, cz, r);
            // Also treat model as solid: if sphere center is inside the mesh, count as intersection
            if (!hit && IsPointInsideAnyMesh(cx, cy, cz)) hit = true;
            ShowSphereOverlay(cx, cy, cz, r, hit);
            MessageBox.Show(this, hit ? "Intersection: YES" : "Intersection: NO", "Sphere vs KDTree");
        }

        private void CylinderIntersectMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var text = VB.Interaction.InputBox("x0,y0,z0,x1,y1,z1,r", "Cylinder Intersection", "0,0,-1,0,0,1,0.5", -1, -1);
            if (string.IsNullOrWhiteSpace(text)) return;
            var p = text.Split(',');
            if (p.Length != 7) { MessageBox.Show(this, "Enter: x0,y0,z0,x1,y1,z1,r"); return; }
            if (!double.TryParse(p[0], out var x0) || !double.TryParse(p[1], out var y0) || !double.TryParse(p[2], out var z0) ||
                !double.TryParse(p[3], out var x1) || !double.TryParse(p[4], out var y1) || !double.TryParse(p[5], out var z1) ||
                !double.TryParse(p[6], out var r)) { MessageBox.Show(this, "Invalid numbers"); return; }

            var tree = EnsureNativeTree();
            if (tree == IntPtr.Zero) { MessageBox.Show(this, "No points to test."); return; }
            bool hit = GML.Interop.Gml.KDTreeCylinderIntersects(tree, x0, y0, z0, x1, y1, z1, r);
            // Solid model: if cylinder axis midpoint is inside mesh, count as intersection
            if (!hit)
            {
                double mx = 0.5 * (x0 + x1), my = 0.5 * (y0 + y1), mz = 0.5 * (z0 + z1);
                if (IsPointInsideAnyMesh(mx, my, mz)) hit = true;
            }
            // Fallback: robust surface test — treat cylinder as radius around its axis segment and
            // check triangle-surface proximity. If any triangle gets within r of the axis (including direct
            // segment-triangle intersection), report intersection.
            if (!hit && CylinderIntersectsMesh(new Vector3((float)x0,(float)y0,(float)z0), new Vector3((float)x1,(float)y1,(float)z1), (float)r))
            {
                hit = true;
            }
            ShowCylinderOverlay(x0, y0, z0, x1, y1, z1, r, hit);
            MessageBox.Show(this, hit ? "Intersection: YES" : "Intersection: NO", "Cylinder vs KDTree");
        }

        private void ConeIntersectMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var text = VB.Interaction.InputBox("x_apex,y_apex,z_apex,x_base,y_base,z_base,r_base", "Cone Intersection", "0,0,-1,0,0,1,1", -1, -1);
            if (string.IsNullOrWhiteSpace(text)) return;
            var p = text.Split(',');
            if (p.Length != 7) { MessageBox.Show(this, "Enter: x0,y0,z0,x1,y1,z1,r_base"); return; }
            if (!double.TryParse(p[0], out var x0) || !double.TryParse(p[1], out var y0) || !double.TryParse(p[2], out var z0) ||
                !double.TryParse(p[3], out var x1) || !double.TryParse(p[4], out var y1) || !double.TryParse(p[5], out var z1) ||
                !double.TryParse(p[6], out var r)) { MessageBox.Show(this, "Invalid numbers"); return; }

            var tree = EnsureNativeTree();
            if (tree == IntPtr.Zero) { MessageBox.Show(this, "No points to test."); return; }
            bool hit = GML.Interop.Gml.KDTreeConeIntersects(tree, x0, y0, z0, x1, y1, z1, r);
            // Solid model: test midpoint along axis as interior sample
            if (!hit)
            {
                double mx = 0.5 * (x0 + x1), my = 0.5 * (y0 + y1), mz = 0.5 * (z0 + z1);
                if (IsPointInsideAnyMesh(mx, my, mz)) hit = true;
            }
            ShowConeOverlay(x0, y0, z0, x1, y1, z1, r, hit);
            MessageBox.Show(this, hit ? "Intersection: YES" : "Intersection: NO", "Cone vs KDTree");
        }

        private IEnumerable<(System.Windows.Media.Media3D.Point3D point, Hx.MeshGeometryModel3D? model)> EnumerateWorldPoints()
        {
            foreach (var t in _triangulations)
            {
                var mat = t.Transform.Value;
                foreach (var v in t.Positions)
                {
                    var p = new System.Windows.Media.Media3D.Point3D(v.X, v.Y, v.Z);
                    yield return (mat.Transform(p), t.Model);
                }
            }
        }

        // ===== Uniform grid build and queries =====
        private static void BuildGrid(Triangulation t)
        {
            // Choose grid resolution ~32 cells along the longest axis (clamped)
            var ext = new Vector3(t.LocalMax.X - t.LocalMin.X, t.LocalMax.Y - t.LocalMin.Y, t.LocalMax.Z - t.LocalMin.Z);
            float longest = Math.Max(ext.X, Math.Max(ext.Y, ext.Z));
            int baseDiv = Math.Clamp((int)Math.Ceiling(longest / Math.Max(longest / 32f, 1e-3f)), 1, 64);
            int gx = Math.Max(1, (int)Math.Ceiling(baseDiv * (ext.X / Math.Max(longest, 1e-6f))));
            int gy = Math.Max(1, (int)Math.Ceiling(baseDiv * (ext.Y / Math.Max(longest, 1e-6f))));
            int gz = Math.Max(1, (int)Math.Ceiling(baseDiv * (ext.Z / Math.Max(longest, 1e-6f))));
            t.GridDims = (gx, gy, gz);
            t.GridOrigin = t.LocalMin;
            t.CellSize = new Vector3(ext.X / Math.Max(gx,1), ext.Y / Math.Max(gy,1), ext.Z / Math.Max(gz,1));
            if (t.CellSize.X <= 0) t.CellSize.X = 1e-3f;
            if (t.CellSize.Y <= 0) t.CellSize.Y = 1e-3f;
            if (t.CellSize.Z <= 0) t.CellSize.Z = 1e-3f;
            t.Grid.Clear();

            // Insert triangles into overlapping cells based on local-space triangle AABBs
            for (int i = 0; i < t.Indices.Length; i += 3)
            {
                var a = t.Positions[t.Indices[i]];
                var b = t.Positions[t.Indices[i + 1]];
                var c = t.Positions[t.Indices[i + 2]];
                LocalTriAabb(a, b, c, out var mn, out var mx);
                CellRangeForAabb(t, mn, mx, out var imin, out var imax);
                for (int ix = imin.x; ix <= imax.x; ix++)
                    for (int iy = imin.y; iy <= imax.y; iy++)
                        for (int iz = imin.z; iz <= imax.z; iz++)
                        {
                            var key = (ix, iy, iz);
                            if (!t.Grid.TryGetValue(key, out var list)) { list = new List<int>(); t.Grid[key] = list; }
                            list.Add(i);
                        }
            }
        }

        private static void LocalTriAabb(in Vector3 a, in Vector3 b, in Vector3 c, out Vector3 mn, out Vector3 mx)
        {
            mn = new Vector3(Math.Min(a.X, Math.Min(b.X, c.X)), Math.Min(a.Y, Math.Min(b.Y, c.Y)), Math.Min(a.Z, Math.Min(b.Z, c.Z)));
            mx = new Vector3(Math.Max(a.X, Math.Max(b.X, c.X)), Math.Max(a.Y, Math.Max(b.Y, c.Y)), Math.Max(a.Z, Math.Max(b.Z, c.Z)));
        }

        private static (int x,int y,int z) CellOf(Triangulation t, in Vector3 p)
        {
            int ix = (int)Math.Floor((p.X - t.GridOrigin.X) / t.CellSize.X);
            int iy = (int)Math.Floor((p.Y - t.GridOrigin.Y) / t.CellSize.Y);
            int iz = (int)Math.Floor((p.Z - t.GridOrigin.Z) / t.CellSize.Z);
            return (ix, iy, iz);
        }

        private static void CellRangeForAabb(Triangulation t, in Vector3 mn, in Vector3 mx, out (int x,int y,int z) imin, out (int x,int y,int z) imax)
        {
            var c0 = CellOf(t, mn);
            var c1 = CellOf(t, mx);
            int x0 = Math.Min(c0.x, c1.x), x1 = Math.Max(c0.x, c1.x);
            int y0 = Math.Min(c0.y, c1.y), y1 = Math.Max(c0.y, c1.y);
            int z0 = Math.Min(c0.z, c1.z), z1 = Math.Max(c0.z, c1.z);
            x0 = Math.Clamp(x0, 0, t.GridDims.X - 1); x1 = Math.Clamp(x1, 0, t.GridDims.X - 1);
            y0 = Math.Clamp(y0, 0, t.GridDims.Y - 1); y1 = Math.Clamp(y1, 0, t.GridDims.Y - 1);
            z0 = Math.Clamp(z0, 0, t.GridDims.Z - 1); z1 = Math.Clamp(z1, 0, t.GridDims.Z - 1);
            imin = (x0, y0, z0); imax = (x1, y1, z1);
        }

        private double[] CollectWorldXYZ()
        {
            // Flattens transformed vertices of ALL triangulations into one array
            int total = 0;
            foreach (var t in _triangulations) total += t.Positions.Length;
            var list = new List<double>(capacity: total * 3);
            foreach (var t in _triangulations)
            {
                var mat = t.Transform.Value;
                foreach (var v in t.Positions)
                {
                    var p = mat.Transform(new System.Windows.Media.Media3D.Point3D(v.X, v.Y, v.Z));
                    list.Add(p.X); list.Add(p.Y); list.Add(p.Z);
                }
            }
            return list.ToArray();
        }

        // ===== Export =====
        private void ExportMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_triangulations.Count == 0) { MessageBox.Show(this, "No imported mesh to export."); return; }
            var dlg = new SaveFileDialog { Title = "Export OBJ", Filter = "Wavefront OBJ (*.obj)|*.obj" };
            if (dlg.ShowDialog(this) != true) return;

            using var sw = new StreamWriter(dlg.FileName);
            sw.WriteLine("# Exported from GML_WPF");
            int vertexBase = 1;
            for (int mi = 0; mi < _triangulations.Count; mi++)
            {
                var t = _triangulations[mi];
                var mat = t.Transform.Value;
                sw.WriteLine($"o mesh_{mi}");
                foreach (var v in t.Positions)
                {
                    var p = mat.Transform(new System.Windows.Media.Media3D.Point3D(v.X, v.Y, v.Z));
                    sw.WriteLine($"v {p.X:F6} {p.Y:F6} {p.Z:F6}");
                }
                for (int i = 0; i < t.Indices.Length; i += 3)
                {
                    int a = t.Indices[i] + vertexBase;
                    int b = t.Indices[i + 1] + vertexBase;
                    int c = t.Indices[i + 2] + vertexBase;
                    sw.WriteLine($"f {a} {b} {c}");
                }
                vertexBase += t.Positions.Length;
            }
            MessageBox.Show(this, "Export complete.", "OBJ Export", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        private void MarkKdDirty() { _kdDirty = true; }

        private readonly List<Hx.MeshGeometryModel3D> _overlays = new();
        private void ClearOverlays()
        {
            foreach (var o in _overlays) View3D.Items.Remove(o);
            _overlays.Clear();
        }

        private void ShowSphereOverlay(double cx, double cy, double cz, double r, bool hit)
        {
            ClearOverlays();
            var geom = BuildSphere((float)cx, (float)cy, (float)cz, (float)r, 32, 16);
            var model = new Hx.MeshGeometryModel3D
            {
                Geometry = geom,
                Material = Hx.PhongMaterials.Gray,
                RenderWireframe = true,
                WireframeColor = hit ? Colors.Lime : Colors.Red
            };
            View3D.Items.Add(model);
            _overlays.Add(model);
        }

        private void ShowCylinderOverlay(double x0, double y0, double z0, double x1, double y1, double z1, double r, bool hit)
        {
            ClearOverlays();
            var geom = BuildCylinder(new Vector3((float)x0, (float)y0, (float)z0), new Vector3((float)x1, (float)y1, (float)z1), (float)r, 36);
            var model = new Hx.MeshGeometryModel3D
            {
                Geometry = geom,
                Material = Hx.PhongMaterials.Gray,
                RenderWireframe = true,
                WireframeColor = hit ? Colors.Lime : Colors.Red
            };
            View3D.Items.Add(model);
            _overlays.Add(model);
        }

        private void ShowConeOverlay(double x0, double y0, double z0, double x1, double y1, double z1, double r, bool hit)
        {
            ClearOverlays();
            var geom = BuildCone(new Vector3((float)x0, (float)y0, (float)z0), new Vector3((float)x1, (float)y1, (float)z1), (float)r, 36);
            var model = new Hx.MeshGeometryModel3D
            {
                Geometry = geom,
                Material = Hx.PhongMaterials.Gray,
                RenderWireframe = true,
                WireframeColor = hit ? Colors.Lime : Colors.Red
            };
            View3D.Items.Add(model);
            _overlays.Add(model);
        }

        private void ShowPointOverlay(double x, double y, double z)
        {
            ClearOverlays();
            // Determine a small radius based on scene AABB
            if (!TryGetSceneAabb(out var smin, out var smax)) return;
            var diag = Math.Sqrt((smax.X - smin.X) * (smax.X - smin.X) + (smax.Y - smin.Y) * (smax.Y - smin.Y) + (smax.Z - smin.Z) * (smax.Z - smin.Z));
            var r = Math.Max(1e-3, 0.01 * diag);
            var geom = BuildSphere((float)x, (float)y, (float)z, (float)r, 16, 8);
            var model = new Hx.MeshGeometryModel3D
            {
                Geometry = geom,
                Material = Hx.PhongMaterials.Blue,
                RenderWireframe = true,
                WireframeColor = Colors.Yellow
            };
            View3D.Items.Add(model);
            _overlays.Add(model);
        }

        private IntPtr EnsureNativeTree()
        {
            if (!_kdDirty && _kdTree != IntPtr.Zero) return _kdTree;
            if (_kdTree != IntPtr.Zero) { GML.Interop.Gml.KDTreeDestroy(_kdTree); _kdTree = IntPtr.Zero; }
            var xyz = CollectWorldXYZ();
            if (xyz.Length == 0) return IntPtr.Zero;
            _kdTree = GML.Interop.Gml.KDTreeCreateFromXYZ(xyz, xyz.Length / 3);
            _kdPointCount = xyz.Length / 3;
            _kdDirty = false;
            return _kdTree;
        }

        private bool TryGetSceneAabb(out Vector3 min, out Vector3 max)
        {
            min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            bool any = false;
            foreach (var t in _triangulations)
            {
                GetWorldAabb(t, out var wmin, out var wmax);
                if (!any) { min = wmin; max = wmax; any = true; }
                else
                {
                    if (wmin.X < min.X) min.X = wmin.X; if (wmin.Y < min.Y) min.Y = wmin.Y; if (wmin.Z < min.Z) min.Z = wmin.Z;
                    if (wmax.X > max.X) max.X = wmax.X; if (wmax.Y > max.Y) max.Y = wmax.Y; if (wmax.Z > max.Z) max.Z = wmax.Z;
                }
            }
            return any;
        }

        // ==== Simple tessellation helpers for overlays ====
        private static HxSharpDX.MeshGeometry3D BuildSphere(float cx, float cy, float cz, float r, int thetaDiv, int phiDiv)
        {
            var positions = new HT.Vector3Collection();
            var indices = new HT.IntCollection();
            for (int pi = 0; pi <= phiDiv; pi++)
            {
                float v = (float)pi / phiDiv; // 0..1
                float phi = v * (float)Math.PI; // 0..PI
                float y = (float)Math.Cos(phi);
                float sr = (float)Math.Sin(phi);
                for (int ti = 0; ti <= thetaDiv; ti++)
                {
                    float u = (float)ti / thetaDiv; // 0..1
                    float theta = u * 2f * (float)Math.PI;
                    float x = (float)Math.Cos(theta) * sr;
                    float z = (float)Math.Sin(theta) * sr;
                    positions.Add(new Vector3(cx + r * x, cy + r * y, cz + r * z));
                }
            }
            int stride = thetaDiv + 1;
            for (int pi = 0; pi < phiDiv; pi++)
            {
                for (int ti = 0; ti < thetaDiv; ti++)
                {
                    int i0 = pi * stride + ti;
                    int i1 = i0 + 1;
                    int i2 = i0 + stride;
                    int i3 = i2 + 1;
                    indices.Add(i0); indices.Add(i2); indices.Add(i1);
                    indices.Add(i1); indices.Add(i2); indices.Add(i3);
                }
            }
            return new HxSharpDX.MeshGeometry3D { Positions = positions, Indices = indices };
        }

        private static Vector3 Normalize(Vector3 v)
        {
            float len = (float)Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
            return len > 1e-6f ? new Vector3(v.X / len, v.Y / len, v.Z / len) : new Vector3(0, 0, 1);
        }

        private static void OrthonormalBasis(Vector3 n, out Vector3 t, out Vector3 b)
        {
            n = Normalize(n);
            Vector3 up = Math.Abs(n.Z) < 0.9f ? new Vector3(0, 0, 1) : new Vector3(0, 1, 0);
            t = Normalize(new Vector3(n.Y * up.Z - n.Z * up.Y, n.Z * up.X - n.X * up.Z, n.X * up.Y - n.Y * up.X));
            b = new Vector3(n.Y * t.Z - n.Z * t.Y, n.Z * t.X - n.X * t.Z, n.X * t.Y - n.Y * t.X);
        }

        private static HxSharpDX.MeshGeometry3D BuildCylinder(Vector3 p0, Vector3 p1, float r, int thetaDiv)
        {
            var positions = new HT.Vector3Collection();
            var indices = new HT.IntCollection();
            var axis = new Vector3(p1.X - p0.X, p1.Y - p0.Y, p1.Z - p0.Z);
            OrthonormalBasis(axis, out var t, out var b);
            // two rings
            var bottomIdx = new List<int>(thetaDiv);
            var topIdx = new List<int>(thetaDiv);
            for (int i = 0; i < thetaDiv; i++)
            {
                float ang = i * 2f * (float)Math.PI / thetaDiv;
                var dir = new Vector3(t.X * (float)Math.Cos(ang) + b.X * (float)Math.Sin(ang),
                                       t.Y * (float)Math.Cos(ang) + b.Y * (float)Math.Sin(ang),
                                       t.Z * (float)Math.Cos(ang) + b.Z * (float)Math.Sin(ang));
                var pb = new Vector3(p0.X + r * dir.X, p0.Y + r * dir.Y, p0.Z + r * dir.Z);
                var pt = new Vector3(p1.X + r * dir.X, p1.Y + r * dir.Y, p1.Z + r * dir.Z);
                bottomIdx.Add(positions.Count);
                positions.Add(pb);
                topIdx.Add(positions.Count);
                positions.Add(pt);
            }
            for (int i = 0; i < thetaDiv; i++)
            {
                int i0 = bottomIdx[i];
                int i1 = topIdx[i];
                int i2 = bottomIdx[(i + 1) % thetaDiv];
                int i3 = topIdx[(i + 1) % thetaDiv];
                indices.Add(i0); indices.Add(i1); indices.Add(i2);
                indices.Add(i1); indices.Add(i3); indices.Add(i2);
            }
            return new HxSharpDX.MeshGeometry3D { Positions = positions, Indices = indices };
        }

        private static HxSharpDX.MeshGeometry3D BuildCone(Vector3 apex, Vector3 baseCenter, float r, int thetaDiv)
        {
            var positions = new HT.Vector3Collection();
            var indices = new HT.IntCollection();
            int apexIndex = 0;
            positions.Add(apex);
            var axis = new Vector3(baseCenter.X - apex.X, baseCenter.Y - apex.Y, baseCenter.Z - apex.Z);
            OrthonormalBasis(axis, out var t, out var b);
            var ring = new List<int>(thetaDiv);
            for (int i = 0; i < thetaDiv; i++)
            {
                float ang = i * 2f * (float)Math.PI / thetaDiv;
                var dir = new Vector3(t.X * (float)Math.Cos(ang) + b.X * (float)Math.Sin(ang),
                                       t.Y * (float)Math.Cos(ang) + b.Y * (float)Math.Sin(ang),
                                       t.Z * (float)Math.Cos(ang) + b.Z * (float)Math.Sin(ang));
                var p = new Vector3(baseCenter.X + r * dir.X, baseCenter.Y + r * dir.Y, baseCenter.Z + r * dir.Z);
                ring.Add(positions.Count);
                positions.Add(p);
            }
            for (int i = 0; i < thetaDiv; i++)
            {
                int i0 = ring[i];
                int i1 = ring[(i + 1) % thetaDiv];
                indices.Add(apexIndex); indices.Add(i0); indices.Add(i1);
            }
            return new HxSharpDX.MeshGeometry3D { Positions = positions, Indices = indices };
        }

        // ==== Solid test via ray casting against combined triangulations ====
        private bool IsPointInsideAnyMesh(double x, double y, double z)
        {
            var origin = new System.Windows.Media.Media3D.Point3D(x, y, z);
            var dir = new System.Windows.Media.Media3D.Vector3D(1, 0, 0); // +X ray
            foreach (var t in _triangulations)
            {
                // AABB cull: skip if point is outside this mesh's world-space AABB
                GetWorldAabb(t, out var wmin, out var wmax);
                if (x < wmin.X || x > wmax.X || y < wmin.Y || y > wmax.Y || z < wmin.Z || z > wmax.Z)
                    continue;
                if (PointInMesh(origin, dir, t)) return true;
            }
            return false;
        }

        private static bool PointInMesh(System.Windows.Media.Media3D.Point3D p, System.Windows.Media.Media3D.Vector3D dir, Triangulation tri)
        {
            // Transform ray into triangulation local space
            var inv = tri.Transform.Value; if (!inv.HasInverse) return false; inv.Invert();
            var p2 = new System.Windows.Media.Media3D.Point3D(p.X + dir.X, p.Y + dir.Y, p.Z + dir.Z);
            var oL = inv.Transform(p);
            var qL = inv.Transform(p2);
            var dL = qL - oL;
            // Epsilon shift to avoid surface start
            oL = new System.Windows.Media.Media3D.Point3D(oL.X + 1e-6, oL.Y + 1e-6, oL.Z + 1e-6);

            // Use grid acceleration: traverse cells along +X local direction by sampling a small YZ band
            var oy = (float)oL.Y; var oz = (float)oL.Z; var ox = (float)oL.X;
            var cell = CellOf(tri, new Vector3(ox, oy, oz));
            int cy = Math.Clamp(cell.y, 0, tri.GridDims.Y - 1);
            int cz = Math.Clamp(cell.z, 0, tri.GridDims.Z - 1);
            int cx = Math.Clamp(cell.x, 0, tri.GridDims.X - 1);
            var visited = new HashSet<int>();
            int hits = 0;
            for (int x = cx; x < tri.GridDims.X; x++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    int yy = cy + dy; if (yy < 0 || yy >= tri.GridDims.Y) continue;
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        int zz = cz + dz; if (zz < 0 || zz >= tri.GridDims.Z) continue;
                        if (tri.Grid.TryGetValue((x, yy, zz), out var list))
                        {
                            foreach (var i in list)
                            {
                                if (!visited.Add(i)) continue;
                                var a = tri.Positions[tri.Indices[i]];
                                var b = tri.Positions[tri.Indices[i + 1]];
                                var c = tri.Positions[tri.Indices[i + 2]];
                                var A = new System.Windows.Media.Media3D.Point3D(a.X, a.Y, a.Z);
                                var B = new System.Windows.Media.Media3D.Point3D(b.X, b.Y, b.Z);
                                var C = new System.Windows.Media.Media3D.Point3D(c.X, c.Y, c.Z);
                                if (RayTriangleIntersect(oL, dL, A, B, C)) hits++;
                            }
                        }
                    }
                }
            }
            return (hits % 2) == 1;
        }

        private static System.Windows.Media.Media3D.Point3D ToP3D(System.Windows.Media.Media3D.Matrix3D m, Vector3 v)
        {
            var p = new System.Windows.Media.Media3D.Point3D(v.X, v.Y, v.Z);
            return m.Transform(p);
        }

        private static void ComputeLocalAabb(IReadOnlyList<Vector3> positions, out Vector3 min, out Vector3 max)
        {
            if (positions.Count == 0) { min = max = new Vector3(0, 0, 0); return; }
            float minX = positions[0].X, minY = positions[0].Y, minZ = positions[0].Z;
            float maxX = minX, maxY = minY, maxZ = minZ;
            for (int i = 1; i < positions.Count; i++)
            {
                var v = positions[i];
                if (v.X < minX) minX = v.X; if (v.Y < minY) minY = v.Y; if (v.Z < minZ) minZ = v.Z;
                if (v.X > maxX) maxX = v.X; if (v.Y > maxY) maxY = v.Y; if (v.Z > maxZ) maxZ = v.Z;
            }
            min = new Vector3(minX, minY, minZ);
            max = new Vector3(maxX, maxY, maxZ);
        }

        private static void GetWorldAabb(Triangulation t, out Vector3 min, out Vector3 max)
        {
            // Transform axis-aligned box using center/extent method
            var Lmin = t.LocalMin; var Lmax = t.LocalMax;
            var center = new Vector3((Lmin.X + Lmax.X) * 0.5f, (Lmin.Y + Lmax.Y) * 0.5f, (Lmin.Z + Lmax.Z) * 0.5f);
            var extent = new Vector3((Lmax.X - Lmin.X) * 0.5f, (Lmax.Y - Lmin.Y) * 0.5f, (Lmax.Z - Lmin.Z) * 0.5f);
            var m = t.Transform.Value;
            // rotation/scale part
            float m11 = (float)m.M11, m12 = (float)m.M12, m13 = (float)m.M13;
            float m21 = (float)m.M21, m22 = (float)m.M22, m23 = (float)m.M23;
            float m31 = (float)m.M31, m32 = (float)m.M32, m33 = (float)m.M33;
            // world center
            var wc = m.Transform(new System.Windows.Media.Media3D.Point3D(center.X, center.Y, center.Z));
            // world extents = |R| * extent
            float ex = Math.Abs(m11) * extent.X + Math.Abs(m12) * extent.Y + Math.Abs(m13) * extent.Z;
            float ey = Math.Abs(m21) * extent.X + Math.Abs(m22) * extent.Y + Math.Abs(m23) * extent.Z;
            float ez = Math.Abs(m31) * extent.X + Math.Abs(m32) * extent.Y + Math.Abs(m33) * extent.Z;
            min = new Vector3((float)wc.X - ex, (float)wc.Y - ey, (float)wc.Z - ez);
            max = new Vector3((float)wc.X + ex, (float)wc.Y + ey, (float)wc.Z + ez);
        }

        private static bool AabbOverlaps(Vector3 aMin, Vector3 aMax, Vector3 bMin, Vector3 bMax)
        {
            if (aMax.X < bMin.X || aMin.X > bMax.X) return false;
            if (aMax.Y < bMin.Y || aMin.Y > bMax.Y) return false;
            if (aMax.Z < bMin.Z || aMin.Z > bMax.Z) return false;
            return true;
        }

        private static bool RayTriangleIntersect(System.Windows.Media.Media3D.Point3D orig, System.Windows.Media.Media3D.Vector3D dir,
                                                 System.Windows.Media.Media3D.Point3D a,
                                                 System.Windows.Media.Media3D.Point3D b,
                                                 System.Windows.Media.Media3D.Point3D c)
        {
            // Moller-Trumbore in double precision (ray)
            double eps = 1e-9;
            var edge1 = b - a; var edge2 = c - a;
            var pvec = System.Windows.Media.Media3D.Vector3D.CrossProduct(dir, edge2);
            double det = System.Windows.Media.Media3D.Vector3D.DotProduct(edge1, pvec);
            if (Math.Abs(det) < eps) return false;
            double invDet = 1.0 / det;
            var tvec = orig - a;
            double u = System.Windows.Media.Media3D.Vector3D.DotProduct(tvec, pvec) * invDet;
            if (u < 0.0 || u > 1.0) return false;
            var qvec = System.Windows.Media.Media3D.Vector3D.CrossProduct(tvec, edge1);
            double v = System.Windows.Media.Media3D.Vector3D.DotProduct(dir, qvec) * invDet;
            if (v < 0.0 || u + v > 1.0) return false;
            double t = System.Windows.Media.Media3D.Vector3D.DotProduct(edge2, qvec) * invDet;
            return t > eps; // intersection along +dir
        }

        private static bool SegmentTriangleIntersect(Vector3 p0, Vector3 p1, System.Windows.Media.Media3D.Point3D a, System.Windows.Media.Media3D.Point3D b, System.Windows.Media.Media3D.Point3D c)
        {
            var d = new System.Windows.Media.Media3D.Vector3D(p1.X - p0.X, p1.Y - p0.Y, p1.Z - p0.Z);
            var o = new System.Windows.Media.Media3D.Point3D(p0.X, p0.Y, p0.Z);
            // Ray test
            double eps = 1e-9;
            var edge1 = b - a; var edge2 = c - a;
            var pvec = System.Windows.Media.Media3D.Vector3D.CrossProduct(d, edge2);
            double det = System.Windows.Media.Media3D.Vector3D.DotProduct(edge1, pvec);
            if (Math.Abs(det) < eps) return false;
            double invDet = 1.0 / det;
            var tvec = o - a;
            double u = System.Windows.Media.Media3D.Vector3D.DotProduct(tvec, pvec) * invDet;
            if (u < 0.0 || u > 1.0) return false;
            var qvec = System.Windows.Media.Media3D.Vector3D.CrossProduct(tvec, edge1);
            double v = System.Windows.Media.Media3D.Vector3D.DotProduct(d, qvec) * invDet;
            if (v < 0.0 || u + v > 1.0) return false;
            double t = System.Windows.Media.Media3D.Vector3D.DotProduct(edge2, qvec) * invDet;
            return t > eps && t < 1.0 + eps; // within segment
        }

        private static double SegmentSegmentDistance(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
        {
            // Returns minimal distance between two 3D segments
            const float EPS = 1e-6f;
            Vector3 d1 = new Vector3(q1.X - p1.X, q1.Y - p1.Y, q1.Z - p1.Z);
            Vector3 d2 = new Vector3(q2.X - p2.X, q2.Y - p2.Y, q2.Z - p2.Z);
            Vector3 r = new Vector3(p1.X - p2.X, p1.Y - p2.Y, p1.Z - p2.Z);
            float a = Dot(d1, d1); // squared length of segment S1
            float e = Dot(d2, d2); // squared length of segment S2
            float f = Dot(d2, r);
            float s, t;
            if (a <= EPS && e <= EPS)
            {
                // both segments degenerate
                return Len(new Vector3(p1.X - p2.X, p1.Y - p2.Y, p1.Z - p2.Z));
            }
            if (a <= EPS)
            {
                // first degenerate
                s = 0; t = Clamp(f / e, 0, 1);
            }
            else
            {
                float c = Dot(d1, r);
                if (e <= EPS)
                {
                    t = 0; s = Clamp(-c / a, 0, 1);
                }
                else
                {
                    float b = Dot(d1, d2);
                    float denom = a * e - b * b;
                    if (denom != 0)
                        s = Clamp((b * f - c * e) / denom, 0, 1);
                    else
                        s = 0;
                    t = (b * s + f) / e;
                    if (t < 0) { t = 0; s = Clamp(-c / a, 0, 1); }
                    else if (t > 1) { t = 1; s = Clamp((b - c) / a, 0, 1); }
                }
            }
            Vector3 c1 = new Vector3(p1.X + s * d1.X, p1.Y + s * d1.Y, p1.Z + s * d1.Z);
            Vector3 c2 = new Vector3(p2.X + t * d2.X, p2.Y + t * d2.Y, p2.Z + t * d2.Z);
            return Len(new Vector3(c1.X - c2.X, c1.Y - c2.Y, c1.Z - c2.Z));
        }

        private static float Dot(Vector3 a, Vector3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        private static float Len(Vector3 a) => (float)Math.Sqrt(a.X * a.X + a.Y * a.Y + a.Z * a.Z);
        private static float Clamp(float x, float lo, float hi) => x < lo ? lo : (x > hi ? hi : x);

        private bool CylinderIntersectsMesh(Vector3 p0, Vector3 p1, float r)
        {
            // Cylinder AABB for quick cull (world)
            var cmin = new Vector3(Math.Min(p0.X, p1.X) - r, Math.Min(p0.Y, p1.Y) - r, Math.Min(p0.Z, p1.Z) - r);
            var cmax = new Vector3(Math.Max(p0.X, p1.X) + r, Math.Max(p0.Y, p1.Y) + r, Math.Max(p0.Z, p1.Z) + r);
            foreach (var t in _triangulations)
            {
                // Skip triangulations whose world AABB doesn't overlap
                GetWorldAabb(t, out var wmin, out var wmax);
                if (!AabbOverlaps(cmin, cmax, wmin, wmax)) continue;

                // Transform cylinder axis into local space
                var inv = t.Transform.Value; if (!inv.HasInverse) continue; inv.Invert();
                var P0 = inv.Transform(new System.Windows.Media.Media3D.Point3D(p0.X, p0.Y, p0.Z));
                var P1 = inv.Transform(new System.Windows.Media.Media3D.Point3D(p1.X, p1.Y, p1.Z));
                var p0L = new Vector3((float)P0.X, (float)P0.Y, (float)P0.Z);
                var p1L = new Vector3((float)P1.X, (float)P1.Y, (float)P1.Z);
                // Conservative local radius scaling: multiply r by max column norm of 3x3
                var m = t.Transform.Value;
                float sx = (float)Math.Sqrt(m.M11 * m.M11 + m.M21 * m.M21 + m.M31 * m.M31);
                float sy = (float)Math.Sqrt(m.M12 * m.M12 + m.M22 * m.M22 + m.M32 * m.M32);
                float sz = (float)Math.Sqrt(m.M13 * m.M13 + m.M23 * m.M23 + m.M33 * m.M33);
                float rL = r * Math.Max(sx, Math.Max(sy, sz));
                var lcmin = new Vector3(Math.Min(p0L.X, p1L.X) - rL, Math.Min(p0L.Y, p1L.Y) - rL, Math.Min(p0L.Z, p1L.Z) - rL);
                var lcmax = new Vector3(Math.Max(p0L.X, p1L.X) + rL, Math.Max(p0L.Y, p1L.Y) + rL, Math.Max(p0L.Z, p1L.Z) + rL);

                // Get candidate triangles from grid cells overlapping the local AABB
                CellRangeForAabb(t, lcmin, lcmax, out var imin, out var imax);
                var visited = new HashSet<int>();
                for (int ix = imin.x; ix <= imax.x; ix++)
                    for (int iy = imin.y; iy <= imax.y; iy++)
                        for (int iz = imin.z; iz <= imax.z; iz++)
                        {
                            if (!t.Grid.TryGetValue((ix, iy, iz), out var list)) continue;
                            foreach (var i in list)
                            {
                                if (!visited.Add(i)) continue;
                                var a = t.Positions[t.Indices[i]];
                                var b = t.Positions[t.Indices[i + 1]];
                                var c = t.Positions[t.Indices[i + 2]];
                                var A = new System.Windows.Media.Media3D.Point3D(a.X, a.Y, a.Z);
                                var B = new System.Windows.Media.Media3D.Point3D(b.X, b.Y, b.Z);
                                var C = new System.Windows.Media.Media3D.Point3D(c.X, c.Y, c.Z);
                                // 1) Direct axis-segment to triangle intersection in local space
                                if (SegmentTriangleIntersect(p0L, p1L, A, B, C)) return true;
                                // 2) Edge distance checks
                                var e0p0 = new Vector3((float)A.X, (float)A.Y, (float)A.Z);
                                var e0p1 = new Vector3((float)B.X, (float)B.Y, (float)B.Z);
                                var e1p0 = new Vector3((float)B.X, (float)B.Y, (float)B.Z);
                                var e1p1 = new Vector3((float)C.X, (float)C.Y, (float)C.Z);
                                var e2p0 = new Vector3((float)C.X, (float)C.Y, (float)C.Z);
                                var e2p1 = new Vector3((float)A.X, (float)A.Y, (float)A.Z);
                                if (SegmentSegmentDistance(p0L, p1L, e0p0, e0p1) <= rL) return true;
                                if (SegmentSegmentDistance(p0L, p1L, e1p0, e1p1) <= rL) return true;
                                if (SegmentSegmentDistance(p0L, p1L, e2p0, e2p1) <= rL) return true;
                                // 3) Endpoint to triangle distance (caps)
                                if (PointTriangleDistance(new Vector3(p0L.X, p0L.Y, p0L.Z), A, B, C) <= rL) return true;
                                if (PointTriangleDistance(new Vector3(p1L.X, p1L.Y, p1L.Z), A, B, C) <= rL) return true;
                            }
                        }
            }
            return false;
        }

        private static float PointTriangleDistance(Vector3 p, System.Windows.Media.Media3D.Point3D a,
                                                   System.Windows.Media.Media3D.Point3D b,
                                                   System.Windows.Media.Media3D.Point3D c)
        {
            // From "Real-Time Collision Detection" (Christer Ericson) style approach
            var A = new Vector3((float)a.X, (float)a.Y, (float)a.Z);
            var B = new Vector3((float)b.X, (float)b.Y, (float)b.Z);
            var C = new Vector3((float)c.X, (float)c.Y, (float)c.Z);
            // Check if P in vertex regions outside A/B/C
            var ab = Sub(B, A); var ac = Sub(C, A); var ap = Sub(p, A);
            float d1 = Dot(ab, ap); float d2 = Dot(ac, ap);
            if (d1 <= 0 && d2 <= 0) return Len(ap);
            var bp = Sub(p, B); float d3 = Dot(ab, bp); float d4 = Dot(ac, bp);
            if (d3 >= 0 && d4 <= d3) return Len(bp);
            var vc = d1 * d4 - d3 * d2;
            if (vc <= 0 && d1 >= 0 && d2 <= 0)
            {
                float v = d1 / (d1 - d2);
                var proj = Add(A, Mul(ab, v));
                return Len(Sub(p, proj));
            }
            var cp = Sub(p, C); float d5 = Dot(ab, cp); float d6 = Dot(ac, cp);
            if (d6 >= 0 && d5 <= d6) return Len(cp);
            var vb = d5 * d2 - d1 * d6;
            if (vb <= 0 && d2 >= 0 && d6 <= 0)
            {
                float w = d2 / (d2 - d6);
                var proj = Add(A, Mul(ac, w));
                return Len(Sub(p, proj));
            }
            var va = d3 * d6 - d5 * d4;
            if (va <= 0 && (d4 - d3) >= 0 && (d5 - d6) >= 0)
            {
                var cb = Sub(C, B);
                float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                var proj = Add(B, Mul(cb, w));
                return Len(Sub(p, proj));
            }
            // Inside face region: distance to plane
            var n = Cross(ab, ac);
            n = Normalize(n);
            float dist = Math.Abs(Dot(Sub(p, A), n));
            return dist;
        }

        private static Vector3 Sub(Vector3 a, Vector3 b) => new Vector3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        private static Vector3 Add(Vector3 a, Vector3 b) => new Vector3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        private static Vector3 Mul(Vector3 a, float s) => new Vector3(a.X * s, a.Y * s, a.Z * s);
        private static Vector3 Cross(Vector3 a, Vector3 b) => new Vector3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    }
}
