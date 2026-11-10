using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;

namespace Aden;

public partial class LaunchWindow : Window
{
    private const int Layers = 7;
    private const double Depth = 0.04;

    public LaunchWindow()
    {
        InitializeComponent();
        Icon = Brand.Mark;
        var model = new Model3DGroup();
        model.Children.Add(new AmbientLight(Colors.White));

        // Logo kalınlığı: ön ve arka yüz tam renkli, aradaki katmanlar koyulaştırılmış siluet.
        // Katmanlar arkadan öne eklenir ki saydam kenarlar doğru karışsın.
        var face = new DiffuseMaterial(new ImageBrush(Brand.Mark));
        var edge = new DiffuseMaterial(new ImageBrush(Brand.Mark)) { AmbientColor = Color.FromRgb(0x7C, 0x8C, 0xC4) };
        face.Freeze();
        edge.Freeze();
        var emblem = new Model3DGroup();
        for (var layer = 0; layer < Layers; layer++)
        {
            var outer = layer == 0 || layer == Layers - 1;
            emblem.Children.Add(LogoPlane((layer - (Layers - 1) / 2.0) * Depth, outer ? face : edge));
        }

        // Logo yalnızca -90°..+90° arasında döner: kenardan göründüğü anda (±90°) başa sarar.
        // Böylece kart hep ön yüzünü gösterir; arka yüz hiç görünmez ve logo asla aynalanmaz.
        var spin = new AxisAngleRotation3D(new Vector3D(0, 1, 0), -90);
        var tilt = new AxisAngleRotation3D(new Vector3D(1, 0, 0), -6);
        emblem.Transform = new Transform3DGroup { Children = { new RotateTransform3D(spin), new RotateTransform3D(tilt) } };
        model.Children.Add(emblem);
        Scene.Children.Add(new ModelVisual3D { Content = model });

        if (SystemParameters.ClientAreaAnimation)
        {
            spin.BeginAnimation(AxisAngleRotation3D.AngleProperty, new DoubleAnimation(-90, 90,
                TimeSpan.FromMilliseconds(700)) { RepeatBehavior = RepeatBehavior.Forever });
            Progress.BeginAnimation(WidthProperty, new DoubleAnimation(30, 130,
                TimeSpan.FromMilliseconds(1100)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
        }
        else
        {
            spin.Angle = 0;
            Progress.Width = 130;
        }
    }

    private static GeometryModel3D LogoPlane(double depth, Material material)
    {
        // Logo 640×668; düzlem aynı en-boy oranında.
        const double width = 1.15, height = 1.2;
        var mesh = new MeshGeometry3D
        {
            Positions = new Point3DCollection
            {
                new(-width, height, depth), new(-width, -height, depth), new(width, -height, depth), new(width, height, depth)
            },
            TextureCoordinates = new PointCollection { new(0, 0), new(0, 1), new(1, 1), new(1, 0) },
            TriangleIndices = new Int32Collection { 0, 1, 2, 0, 2, 3 }
        };
        mesh.Freeze();
        return new GeometryModel3D(mesh, material);
    }
}
