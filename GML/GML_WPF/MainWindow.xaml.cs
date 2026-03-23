using System;
using System.IO;
using System.Linq;
using System.Windows;
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
            // Clear previous imported models (keep lights, etc.)
            for (int i = View3D.Items.Count - 1; i >= 0; i--)
            {
                if (View3D.Items[i] is Hx.MeshGeometryModel3D m && !ReferenceEquals(m.Material, Hx.PhongMaterials.Orange))
                {
                    View3D.Items.RemoveAt(i);
                }
            }

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
                    WireframeColor = System.Windows.Media.Colors.Lime
                };
                View3D.Items.Add(model);
            }
        }
    }
}
