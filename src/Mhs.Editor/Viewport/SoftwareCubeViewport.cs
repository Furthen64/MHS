using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Mhs.Editor.Viewport;

public sealed class SoftwareCubeViewport : Control
{
    private readonly IViewportRenderer _renderer;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private TimeSpan _lastTick;

    public SoftwareCubeViewport()
        : this(new SoftwareCubeRenderer(new CubeSceneState()))
    {
    }

    public SoftwareCubeViewport(IViewportRenderer renderer)
    {
        _renderer = renderer;
        ClipToBounds = true;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };

        _timer.Tick += OnTick;

        AttachedToVisualTree += (_, _) =>
        {
            _lastTick = _stopwatch.Elapsed;
            _timer.Start();
        };

        DetachedFromVisualTree += (_, _) => _timer.Stop();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        _renderer.Render(context, Bounds);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = _stopwatch.Elapsed;
        var delta = now - _lastTick;
        _lastTick = now;

        _renderer.Update(delta);
        InvalidateVisual();
    }
}
