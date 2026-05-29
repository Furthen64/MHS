using System;
using Avalonia;
using Avalonia.Media;

namespace Mhs.Editor.Viewport;

public interface IViewportRenderer
{
    void Update(TimeSpan deltaTime);
    void Render(DrawingContext context, Rect bounds);
}
