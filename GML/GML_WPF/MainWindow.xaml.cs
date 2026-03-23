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
            LookDirection = new Vector3D(0, 0, -8),
            UpDirection = new Vector3D(0, 1, 0),
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
                Material = Hx.PhongMaterials.Orange
            };

            View3D.Items.Add(cubeModel);

            // Optional: handle menu actions like reset camera
            ResetCameraMenuItem.Click += (_, __) =>
            {
                _camera.Position = new Point3D(0, 0, 8);
                _camera.LookDirection = new Vector3D(0, 0, -8);
                _camera.UpDirection = new Vector3D(0, 1, 0);
            };
        }
    }
}
