using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;

namespace Aden;

public partial class LaunchWindow : Window
{
    public LaunchWindow()
    {
        InitializeComponent();
        Icon = Brand.Mark;
        var model = new Model3DGroup();
        model.Children.Add(new AmbientLight(Colors.White));
        var rotation = new AxisAngleRotation3D(new Vector3D(0.12, 1, 0.05), 0);
        var emblem = new Model3DGroup { Transform = new RotateTransform3D(rotation) };
        var face = new DiffuseMaterial(new ImageBrush(Brand.Mark));
        for (var layer = -3; layer <= 3; layer++)
            emblem.Children.Add(LogoPlane(layer * 0.038, face, layer == -3));
        model.Children.Add(emblem);
        Scene.Children.Add(new ModelVisual3D { Content = model });

        if (SystemParameters.ClientAreaAnimation)
        {
            rotation.BeginAnimation(AxisAngleRotation3D.AngleProperty, new DoubleAnimation(0, 360,
                TimeSpan.FromMilliseconds(1100)) { RepeatBehavior = RepeatBehavior.Forever });
            Progress.BeginAnimation(WidthProperty, new DoubleAnimation(30, 130,
                TimeSpan.FromMilliseconds(1100)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
        }
        else
        {
            Progress.Width = 130;
        }
    }

    private static GeometryModel3D LogoPlane(double depth, Material material, bool back)
    {
        var mesh = new MeshGeometry3D
        {
            Positions = new Point3DCollection
            {
                new(-1.15, 1.2, depth), new(-1.15, -1.2, depth), new(1.15, -1.2, depth), new(1.15, 1.2, depth)
            },
            TextureCoordinates = back
                ? new PointCollection { new(1, 0), new(1, 1), new(0, 1), new(0, 0) }
                : new PointCollection { new(0, 0), new(0, 1), new(1, 1), new(1, 0) },
            TriangleIndices = new Int32Collection { 0, 1, 2, 0, 2, 3 }
        };
        return new GeometryModel3D(mesh, material) { BackMaterial = material };
    }
}
