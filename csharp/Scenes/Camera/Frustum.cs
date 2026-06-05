using System.Numerics;

namespace IdleL.Scenes;

// A view frustum as 6 inward-facing planes, extracted from a combined view*projection matrix.
// A bounding sphere is visible if it's inside (or straddling) every plane.
struct Frustum
{
    // each plane = (xyz: normal, w: distance), normalized. Inside a plane <=> dot(normal, p) + w >= 0.
    Vector4 left, right, bottom, top, near, far;

    public static Frustum FromViewProjection(Matrix4x4 m)
    {
        // Gribb–Hartmann, for OUR convention: row-vector (clip = v * m) + Vulkan [0,1] depth.
        // clip.j = dot(v4, column j of m), so the planes are built from the COLUMNS of m.
        // (the Vulkan Y-flip only swaps top<->bottom, same volume so culling is unaffected.)
        Vector4 c0 = new(m.M11, m.M21, m.M31, m.M41);   // column 0  -> clip.x
        Vector4 c1 = new(m.M12, m.M22, m.M32, m.M42);   // column 1  -> clip.y
        Vector4 c2 = new(m.M13, m.M23, m.M33, m.M43);   // column 2  -> clip.z
        Vector4 c3 = new(m.M14, m.M24, m.M34, m.M44);   // column 3  -> clip.w

        Frustum f;
        f.left   = Normalize(c3 + c0);   // x >= -w
        f.right  = Normalize(c3 - c0);   // x <=  w
        f.bottom = Normalize(c3 + c1);   // y >= -w
        f.top    = Normalize(c3 - c1);   // y <=  w
        f.near   = Normalize(c2);        // z >=  0   (Vulkan/D3D [0,1]; OpenGL would be c3 + c2)
        f.far    = Normalize(c3 - c2);   // z <=  w
        return f;
    }

    // normalize so the plane's distance is in world units (needed for the radius comparison)
    static Vector4 Normalize(Vector4 plane)
    {
        float len = new Vector3(plane.X, plane.Y, plane.Z).Length();
        return len > 0f ? plane / len : plane;
    }

    // true if any part of the sphere is inside the frustum (i.e. not fully outside any single plane)
    public bool Intersects(Vector3 center, float radius)
    {
        return Dist(left, center)   >= -radius
            && Dist(right, center)  >= -radius
            && Dist(bottom, center) >= -radius
            && Dist(top, center)    >= -radius
            && Dist(near, center)   >= -radius
            && Dist(far, center)    >= -radius;
    }

    // signed distance from the plane to a point (positive = inside)
    static float Dist(Vector4 plane, Vector3 p) => plane.X * p.X + plane.Y * p.Y + plane.Z * p.Z + plane.W;

    // copy the 6 planes out for GPU culling (same order as the fields)
    public void GetPlanes(Span<Vector4> dst)
    {
        dst[0] = left; dst[1] = right; dst[2] = bottom; dst[3] = top; dst[4] = near; dst[5] = far;
    }
}
