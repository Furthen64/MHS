using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.OpenGL;
using Silk.NET.Core.Contexts;
using Silk.NET.OpenGL;

namespace Mhs.Editor.Viewport.Gl;

public sealed class GlRenderer : IDisposable
{
    private const string VertexShaderSource = """
        #version 330 core
        layout(location = 0) in vec2 aPosition;
        layout(location = 1) in vec4 aColor;

        out vec4 vColor;

        void main()
        {
            gl_Position = vec4(aPosition, 0.0, 1.0);
            vColor = aColor;
        }
        """;

    private const string FragmentShaderSource = """
        #version 330 core
        in vec4 vColor;
        out vec4 FragColor;

        void main()
        {
            FragColor = vColor;
        }
        """;

    [StructLayout(LayoutKind.Sequential)]
    private struct GlVertex
    {
        public float X;
        public float Y;
        public float R;
        public float G;
        public float B;
        public float A;
    }

    private readonly GL _gl;
    private readonly List<GlVertex> _triangles = [];
    private readonly List<GlVertex> _lines = [];

    private uint _program;
    private uint _vao;
    private uint _vbo;
    private bool _disposed;
    private double _width;
    private double _height;

    public GlRenderer(GlInterface glInterface)
    {
        _gl = GL.GetApi(new AvaloniaNativeContext(glInterface));
        InitializeResources();
    }

    public string Vendor => _gl.GetStringS(StringName.Vendor) ?? "Unknown";
    public string Renderer => _gl.GetStringS(StringName.Renderer) ?? "Unknown";
    public string Version => _gl.GetStringS(StringName.Version) ?? "Unknown";

    public void BeginFrame(int framebuffer, Size bounds)
    {
        _width = Math.Max(bounds.Width, 1);
        _height = Math.Max(bounds.Height, 1);

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)framebuffer);
        _gl.Viewport(0, 0, (uint)Math.Round(_width), (uint)Math.Round(_height));
        _gl.ClearColor(25f / 255f, 30f / 255f, 35f / 255f, 1f);
        _gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));

        _triangles.Clear();
        _lines.Clear();
    }

    public void AddFilledQuad(Point a, Point b, Point c, Point d, Color color, double opacity)
    {
        var c0 = ToColor(color, opacity);
        AddTriangleInternal(a, b, c, c0);
        AddTriangleInternal(a, c, d, c0);
    }

    public void AddFilledTriangle(Point a, Point b, Point c, Color color, double opacity)
    {
        AddTriangleInternal(a, b, c, ToColor(color, opacity));
    }

    public void AddLine(Point a, Point b, Color color, double opacity)
    {
        var c0 = ToColor(color, opacity);
        _lines.Add(ToVertex(a, c0));
        _lines.Add(ToVertex(b, c0));
    }

    public void RenderFrame()
    {
        _gl.UseProgram(_program);
        _gl.BindVertexArray(_vao);

        if (_triangles.Count > 0)
        {
            Upload(_triangles);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_triangles.Count);
        }

        if (_lines.Count > 0)
        {
            Upload(_lines);
            _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)_lines.Count);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_vbo != 0)
        {
            _gl.DeleteBuffer(_vbo);
            _vbo = 0;
        }

        if (_vao != 0)
        {
            _gl.DeleteVertexArray(_vao);
            _vao = 0;
        }

        if (_program != 0)
        {
            _gl.DeleteProgram(_program);
            _program = 0;
        }

        _gl.Dispose();
    }

    private unsafe void Upload(List<GlVertex> vertices)
    {
        if (vertices.Count == 0)
        {
            return;
        }

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        var span = CollectionsMarshal.AsSpan(vertices);
        fixed (GlVertex* ptr = span)
        {
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(span.Length * sizeof(GlVertex)), ptr, BufferUsageARB.DynamicDraw);
        }
    }

    private static (float R, float G, float B, float A) ToColor(Color color, double opacity)
    {
        var alpha = Math.Clamp((color.A / 255.0) * opacity, 0, 1);
        return ((float)(color.R / 255.0), (float)(color.G / 255.0), (float)(color.B / 255.0), (float)alpha);
    }

    private void AddTriangleInternal(Point a, Point b, Point c, (float R, float G, float B, float A) color)
    {
        _triangles.Add(ToVertex(a, color));
        _triangles.Add(ToVertex(b, color));
        _triangles.Add(ToVertex(c, color));
    }

    private GlVertex ToVertex(Point point, (float R, float G, float B, float A) color)
    {
        var x = (float)(point.X / _width * 2.0 - 1.0);
        var y = (float)(1.0 - point.Y / _height * 2.0);
        return new GlVertex { X = x, Y = y, R = color.R, G = color.G, B = color.B, A = color.A };
    }

    private unsafe void InitializeResources()
    {
        _program = CreateProgram();

        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();

        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);

        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, (uint)sizeof(GlVertex), (void*)0);

        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, (uint)sizeof(GlVertex), (void*)(2 * sizeof(float)));

        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Lequal);

        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
    }

    private uint CreateProgram()
    {
        var vertexShader = CompileShader(ShaderType.VertexShader, VertexShaderSource);
        var fragmentShader = CompileShader(ShaderType.FragmentShader, FragmentShaderSource);

        var program = _gl.CreateProgram();
        _gl.AttachShader(program, vertexShader);
        _gl.AttachShader(program, fragmentShader);
        _gl.LinkProgram(program);
        _gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out var success);
        if (success == 0)
        {
            var info = _gl.GetProgramInfoLog(program);
            _gl.DeleteShader(vertexShader);
            _gl.DeleteShader(fragmentShader);
            _gl.DeleteProgram(program);
            throw new InvalidOperationException($"OpenGL program link failed: {info}");
        }

        _gl.DeleteShader(vertexShader);
        _gl.DeleteShader(fragmentShader);
        return program;
    }

    private uint CompileShader(ShaderType type, string source)
    {
        var shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);
        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out var success);
        if (success == 0)
        {
            var info = _gl.GetShaderInfoLog(shader);
            _gl.DeleteShader(shader);
            throw new InvalidOperationException($"OpenGL {type} compile failed: {info}");
        }

        return shader;
    }

    private sealed class AvaloniaNativeContext(GlInterface glInterface) : INativeContext
    {
        public nint GetProcAddress(string proc, int? slot = null)
            => glInterface.GetProcAddress(proc);

        public bool TryGetProcAddress(string proc, out nint addr, int? slot = null)
        {
            addr = glInterface.GetProcAddress(proc);
            return addr != 0;
        }

        public void Dispose()
        {
        }
    }
}
