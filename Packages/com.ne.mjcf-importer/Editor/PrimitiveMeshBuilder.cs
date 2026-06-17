using System.Collections.Generic;
using UnityEngine;

namespace Ne.MjcfImporter.Editor
{
    public static class PrimitiveMeshBuilder
    {
        const int DefaultSegments = 24;
        const int DefaultRings = 12;

        public static Mesh CreateBox(Vector3 size)
        {
            Vector3 half = size * 0.5f;
            var vertices = new[]
            {
                new Vector3(-half.x, -half.y, -half.z), new Vector3(half.x, -half.y, -half.z),
                new Vector3(half.x, half.y, -half.z), new Vector3(-half.x, half.y, -half.z),
                new Vector3(-half.x, -half.y, half.z), new Vector3(half.x, -half.y, half.z),
                new Vector3(half.x, half.y, half.z), new Vector3(-half.x, half.y, half.z)
            };
            var triangles = new[]
            {
                0, 2, 1, 0, 3, 2,
                4, 5, 6, 4, 6, 7,
                0, 1, 5, 0, 5, 4,
                2, 3, 7, 2, 7, 6,
                1, 2, 6, 1, 6, 5,
                3, 0, 4, 3, 4, 7
            };
            return BuildMesh("MJCF_Box", vertices, triangles);
        }

        public static Mesh CreateSphere(float radius, int segments = DefaultSegments, int rings = DefaultRings)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            for (int y = 0; y <= rings; y++)
            {
                float v = y / (float)rings;
                float latitude = Mathf.Lerp(-Mathf.PI * 0.5f, Mathf.PI * 0.5f, v);
                float ringRadius = Mathf.Cos(latitude) * radius;
                float height = Mathf.Sin(latitude) * radius;

                for (int x = 0; x <= segments; x++)
                {
                    float u = x / (float)segments;
                    float longitude = u * Mathf.PI * 2f;
                    vertices.Add(new Vector3(Mathf.Cos(longitude) * ringRadius, height, Mathf.Sin(longitude) * ringRadius));
                }
            }

            AddLatLongTriangles(triangles, segments, rings);
            return BuildMesh("MJCF_Sphere", vertices.ToArray(), triangles.ToArray());
        }

        public static Mesh CreateCylinder(float radius, float height, int segments = DefaultSegments)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            float halfHeight = height * 0.5f;

            for (int i = 0; i <= segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                float x = Mathf.Cos(angle) * radius;
                float z = Mathf.Sin(angle) * radius;
                vertices.Add(new Vector3(x, -halfHeight, z));
                vertices.Add(new Vector3(x, halfHeight, z));
            }

            for (int i = 0; i < segments; i++)
            {
                int bottom0 = i * 2;
                int top0 = bottom0 + 1;
                int bottom1 = bottom0 + 2;
                int top1 = bottom0 + 3;
                triangles.Add(bottom0);
                triangles.Add(top0);
                triangles.Add(top1);
                triangles.Add(bottom0);
                triangles.Add(top1);
                triangles.Add(bottom1);
            }

            int bottomCenter = vertices.Count;
            vertices.Add(new Vector3(0f, -halfHeight, 0f));
            int topCenter = vertices.Count;
            vertices.Add(new Vector3(0f, halfHeight, 0f));

            for (int i = 0; i < segments; i++)
            {
                int bottom0 = i * 2;
                int bottom1 = bottom0 + 2;
                int top0 = bottom0 + 1;
                int top1 = bottom0 + 3;
                triangles.Add(bottomCenter);
                triangles.Add(bottom1);
                triangles.Add(bottom0);
                triangles.Add(topCenter);
                triangles.Add(top0);
                triangles.Add(top1);
            }

            return BuildMesh("MJCF_Cylinder", vertices.ToArray(), triangles.ToArray());
        }

        public static Mesh CreateCapsule(float radius, float height, int segments = DefaultSegments, int rings = DefaultRings)
        {
            height = Mathf.Max(height, radius * 2f);
            float cylinderHalf = Mathf.Max(0f, (height - radius * 2f) * 0.5f);
            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            int halfRings = Mathf.Max(2, rings / 2);
            for (int y = 0; y <= rings; y++)
            {
                float t = y / (float)rings;
                float latitude = Mathf.Lerp(-Mathf.PI * 0.5f, Mathf.PI * 0.5f, t);
                float ringRadius = Mathf.Cos(latitude) * radius;
                float centerOffset = latitude >= 0f ? cylinderHalf : -cylinderHalf;
                float heightOffset = Mathf.Sin(latitude) * radius + centerOffset;

                if (Mathf.Abs(latitude) < 0.0001f && halfRings > 0)
                {
                    heightOffset = y < halfRings ? -cylinderHalf : cylinderHalf;
                }

                for (int x = 0; x <= segments; x++)
                {
                    float u = x / (float)segments;
                    float longitude = u * Mathf.PI * 2f;
                    vertices.Add(new Vector3(Mathf.Cos(longitude) * ringRadius, heightOffset, Mathf.Sin(longitude) * ringRadius));
                }
            }

            AddLatLongTriangles(triangles, segments, rings);
            return BuildMesh("MJCF_Capsule", vertices.ToArray(), triangles.ToArray());
        }

        static void AddLatLongTriangles(List<int> triangles, int segments, int rings)
        {
            int stride = segments + 1;
            for (int y = 0; y < rings; y++)
            {
                for (int x = 0; x < segments; x++)
                {
                    int i0 = y * stride + x;
                    int i1 = i0 + 1;
                    int i2 = i0 + stride;
                    int i3 = i2 + 1;
                    triangles.Add(i0);
                    triangles.Add(i2);
                    triangles.Add(i1);
                    triangles.Add(i1);
                    triangles.Add(i2);
                    triangles.Add(i3);
                }
            }
        }

        static Mesh BuildMesh(string name, Vector3[] vertices, int[] triangles)
        {
            var mesh = new Mesh
            {
                name = name,
                vertices = vertices,
                triangles = triangles
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
