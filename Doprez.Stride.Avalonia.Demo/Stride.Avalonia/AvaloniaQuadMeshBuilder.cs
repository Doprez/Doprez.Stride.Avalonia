using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Stride.Core.Mathematics;
using Stride.Graphics;

namespace Stride.Avalonia;

/// <summary>
/// Builds subdivided quad meshes for custom-effect Avalonia panels.
/// The mesh is a flat quad centered at the origin spanning ±0.5 in X/Y
/// with UV coordinates from (0,0) top-left to (1,1) bottom-right.
/// Results are cached per (device, subdivisions) pair.
/// </summary>
internal static class AvaloniaQuadMeshBuilder
{
    /// <summary>
    /// Vertex layout: Position (float4) + TexCoord (float2).
    /// Position uses float4 (w=1) to match the SDSL stream declaration.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct VertexPositionTexture
    {
        public Vector4 Position;
        public Vector2 TexCoord;

        public static readonly VertexDeclaration Layout = new(
            VertexElement.Position<Vector4>(),
            VertexElement.TextureCoordinate<Vector2>());
    }

    /// <summary>
    /// Cached mesh data for a given subdivision count.
    /// </summary>
    public readonly struct QuadMesh
    {
        public readonly Stride.Graphics.Buffer VertexBuffer;
        public readonly Stride.Graphics.Buffer IndexBuffer;
        public readonly int IndexCount;
        public readonly VertexBufferBinding VertexBufferBinding;
        public readonly IndexBufferBinding IndexBufferBinding;

        public QuadMesh(Stride.Graphics.Buffer vertexBuffer, Stride.Graphics.Buffer indexBuffer, int indexCount)
        {
            VertexBuffer = vertexBuffer;
            IndexBuffer = indexBuffer;
            IndexCount = indexCount;
            VertexBufferBinding = new VertexBufferBinding(
                vertexBuffer, VertexPositionTexture.Layout, vertexBuffer.ElementCount);
            IndexBufferBinding = new IndexBufferBinding(indexBuffer, true, indexCount);
        }
    }

    private static readonly Dictionary<int, QuadMesh> _cache = new();

    /// <summary>
    /// Gets or creates a subdivided quad mesh.
    /// </summary>
    /// <param name="device">The graphics device.</param>
    /// <param name="subdivisions">Number of subdivisions per axis (e.g. 32 → 33×33 vertices).</param>
    /// <returns>Cached mesh data with vertex/index buffers.</returns>
    public static QuadMesh GetOrCreate(GraphicsDevice device, int subdivisions)
    {
        subdivisions = Math.Clamp(subdivisions, 1, 256);

        if (_cache.TryGetValue(subdivisions, out var cached))
            return cached;

        var mesh = Build(device, subdivisions);
        _cache[subdivisions] = mesh;
        return mesh;
    }

    private static QuadMesh Build(GraphicsDevice device, int subdivisions)
    {
        int vertsPerSide = subdivisions + 1;
        int vertexCount = vertsPerSide * vertsPerSide;
        int quadCount = subdivisions * subdivisions;
        int indexCount = quadCount * 6; // 2 triangles per quad, 3 indices each

        var vertices = new VertexPositionTexture[vertexCount];
        var indices = new int[indexCount];

        // Generate vertices: quad from (-0.5, -0.5) to (0.5, 0.5)
        // UV from (0, 0) top-left to (1, 1) bottom-right
        for (int y = 0; y < vertsPerSide; y++)
        {
            for (int x = 0; x < vertsPerSide; x++)
            {
                float u = (float)x / subdivisions;
                float v = (float)y / subdivisions;

                int idx = y * vertsPerSide + x;
                vertices[idx] = new VertexPositionTexture
                {
                    Position = new Vector4(u - 0.5f, (1f - v) - 0.5f, 0f, 1f),
                    TexCoord = new Vector2(u, v),
                };
            }
        }

        // Generate indices (two triangles per quad, clockwise winding)
        int ii = 0;
        for (int y = 0; y < subdivisions; y++)
        {
            for (int x = 0; x < subdivisions; x++)
            {
                int topLeft = y * vertsPerSide + x;
                int topRight = topLeft + 1;
                int bottomLeft = topLeft + vertsPerSide;
                int bottomRight = bottomLeft + 1;

                // Triangle 1
                indices[ii++] = topLeft;
                indices[ii++] = topRight;
                indices[ii++] = bottomLeft;

                // Triangle 2
                indices[ii++] = topRight;
                indices[ii++] = bottomRight;
                indices[ii++] = bottomLeft;
            }
        }

        var vertexBuffer = Stride.Graphics.Buffer.Vertex.New(device, vertices, GraphicsResourceUsage.Immutable);
        var indexBuffer = Stride.Graphics.Buffer.Index.New(device, indices, GraphicsResourceUsage.Immutable);

        return new QuadMesh(vertexBuffer, indexBuffer, indexCount);
    }
}
