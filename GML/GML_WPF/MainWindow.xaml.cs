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

namespace GML_WPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly Hx.PerspectiveCamera _camera = new Hx.PerspectiveCamera
        {
            Position = new Point3D(0, 0, 8),
            LookDirection = new System.Windows.Media.Media3D.Vector3D(0, 0, -8),
            UpDirection = new System.Windows.Media.Media3D.Vector3D(0, 1, 0),
            FieldOfView = 45
        };

        private readonly List<Hx.MeshGeometryModel3D> _imported = new();

        public MainWindow()
        {
            InitializeComponent();

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

            View3D.Items.Add(cubeModel);

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

        private void LoadObjToViewport(string path)
        {
            // Remove previously imported models from viewport and list (keep the sample cube)
            foreach (var m in _imported)
            {
                View3D.Items.Remove(m);
            }
            _imported.Clear();

            var context = new AssimpContext();
            var scene = context.ImportFile(path, PostProcessSteps.Triangulate | PostProcessSteps.JoinIdenticalVertices);
            if (scene == null || !scene.HasMeshes)
                throw new InvalidOperationException($"No meshes found in: {Path.GetFileName(path)}");

            foreach (var mesh in scene.Meshes)
            {
                var positions = new HT.Vector3Collection(mesh.VertexCount);
                for (int i = 0; i < mesh.VertexCount; i++)
                {
                    var v = mesh.Vertices[i];
                    positions.Add(new Vector3(v.X, v.Y, v.Z));
                }

                var indices = new HT.IntCollection(mesh.FaceCount * 3);
                foreach (var f in mesh.Faces)
                {
                    if (f.IndexCount == 3)
                    {
                        indices.Add(f.Indices[0]);
                        indices.Add(f.Indices[1]);
                        indices.Add(f.Indices[2]);
                    }
                }

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
                // Ensure each imported model has a transform group so we can edit it later
                model.Transform = model.Transform ?? new Transform3DGroup();

                View3D.Items.Add(model);
                _imported.Add(model);
            }
        }

        // ===== Transform menu handlers =====
        private void ResetModelTransformMenuItem_Click(object sender, RoutedEventArgs e)
        {
            foreach (var m in _imported)
            {
                m.Transform = new Transform3DGroup();
            }
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
            foreach (var m in _imported)
            {
                EnsureTransformGroup(m).Children.Add(new TranslateTransform3D(a, b, c));
            }
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
            foreach (var m in _imported)
            {
                var rot = new AxisAngleRotation3D(new System.Windows.Media.Media3D.Vector3D(ax, ay, az), ang);
                EnsureTransformGroup(m).Children.Add(new RotateTransform3D(rot));
            }
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
            foreach (var m in _imported)
            {
                EnsureTransformGroup(m).Children.Add(new ScaleTransform3D(sx, sy, sz));
            }
        }

        private static Transform3DGroup EnsureTransformGroup(Hx.MeshGeometryModel3D m)
        {
            if (m.Transform is Transform3DGroup g) return g;
            g = new Transform3DGroup();
            m.Transform = g;
            return g;
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
            var xyz = CollectWorldXYZ();
            if (xyz.Length == 0) { MessageBox.Show(this, "No points to test."); return; }
            var tree = GML.Interop.Gml.KDTreeCreateFromXYZ(xyz, xyz.Length / 3);
            try
            {
                bool hit = GML.Interop.Gml.KDTreeSphereIntersects(tree, cx, cy, cz, r);
                MessageBox.Show(this, hit ? "Intersection: YES" : "Intersection: NO", "Sphere vs KDTree");
            }
            finally { GML.Interop.Gml.KDTreeDestroy(tree); }
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

            var xyz = CollectWorldXYZ();
            if (xyz.Length == 0) { MessageBox.Show(this, "No points to test."); return; }
            var tree = GML.Interop.Gml.KDTreeCreateFromXYZ(xyz, xyz.Length / 3);
            try
            {
                bool hit = GML.Interop.Gml.KDTreeCylinderIntersects(tree, x0, y0, z0, x1, y1, z1, r);
                MessageBox.Show(this, hit ? "Intersection: YES" : "Intersection: NO", "Cylinder vs KDTree");
            }
            finally { GML.Interop.Gml.KDTreeDestroy(tree); }
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

            var xyz = CollectWorldXYZ();
            if (xyz.Length == 0) { MessageBox.Show(this, "No points to test."); return; }
            var tree = GML.Interop.Gml.KDTreeCreateFromXYZ(xyz, xyz.Length / 3);
            try
            {
                bool hit = GML.Interop.Gml.KDTreeConeIntersects(tree, x0, y0, z0, x1, y1, z1, r);
                MessageBox.Show(this, hit ? "Intersection: YES" : "Intersection: NO", "Cone vs KDTree");
            }
            finally { GML.Interop.Gml.KDTreeDestroy(tree); }
        }

        private IEnumerable<(System.Windows.Media.Media3D.Point3D point, Hx.MeshGeometryModel3D model)> EnumerateWorldPoints()
        {
            foreach (var m in _imported)
            {
                if (m.Geometry is not HxSharpDX.MeshGeometry3D geom) continue;
                var xf = (m.Transform as Transform3D) ?? Transform3D.Identity;
                var mat = xf.Value;
                foreach (var v in geom.Positions)
                {
                    var p = new System.Windows.Media.Media3D.Point3D(v.X, v.Y, v.Z);
                    yield return (mat.Transform(p), m);
                }
            }
        }

        private double[] CollectWorldXYZ()
        {
            // Flattens transformed vertices to [x0,y0,z0, x1,y1,z1, ...]
            var list = new List<double>(capacity: _imported.Sum(m => ((HxSharpDX.MeshGeometry3D)m.Geometry).Positions.Count) * 3);
            foreach (var m in _imported)
            {
                if (m.Geometry is not HxSharpDX.MeshGeometry3D geom) continue;
                var xf = (m.Transform as Transform3D) ?? Transform3D.Identity;
                var mat = xf.Value;
                foreach (var v in geom.Positions)
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
            if (_imported.Count == 0) { MessageBox.Show(this, "No imported mesh to export."); return; }
            var dlg = new SaveFileDialog { Title = "Export OBJ", Filter = "Wavefront OBJ (*.obj)|*.obj" };
            if (dlg.ShowDialog(this) != true) return;

            using var sw = new StreamWriter(dlg.FileName);
            sw.WriteLine("# Exported from GML_WPF");
            int vertexBase = 1;
            for (int mi = 0; mi < _imported.Count; mi++)
            {
                var m = _imported[mi];
                var geom = (HxSharpDX.MeshGeometry3D)m.Geometry;
                var xf = (m.Transform as Transform3D) ?? Transform3D.Identity;
                var mat = xf.Value;
                sw.WriteLine($"o mesh_{mi}");
                foreach (var v in geom.Positions)
                {
                    var p = mat.Transform(new System.Windows.Media.Media3D.Point3D(v.X, v.Y, v.Z));
                    sw.WriteLine($"v {p.X:F6} {p.Y:F6} {p.Z:F6}");
                }
                for (int i = 0; i < geom.Indices.Count; i += 3)
                {
                    int a = geom.Indices[i] + vertexBase;
                    int b = geom.Indices[i + 1] + vertexBase;
                    int c = geom.Indices[i + 2] + vertexBase;
                    sw.WriteLine($"f {a} {b} {c}");
                }
                vertexBase += geom.Positions.Count;
            }
            MessageBox.Show(this, "Export complete.", "OBJ Export", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
