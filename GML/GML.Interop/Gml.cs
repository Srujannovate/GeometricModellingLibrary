using System;

namespace GML.Interop
{
    public static class Gml
    {
        public static int Add(int a, int b) => GmlNative.Add(a, b);

        public static bool SpheresIntersect(
            double x1, double y1, double z1, double r1,
            double x2, double y2, double z2, double r2)
            => GmlNative.SpheresIntersect(x1, y1, z1, r1, x2, y2, z2, r2) != 0;

        // KDTree helpers intentionally left minimal; need factory/destructor exports in native lib
        public static bool KDTreeSphereIntersects(IntPtr tree, double cx, double cy, double cz, double r)
            => GmlNative.KDTree_SphereIntersects(tree, cx, cy, cz, r) != 0;

        public static bool KDTreeCylinderIntersects(IntPtr tree,
            double x0, double y0, double z0,
            double x1, double y1, double z1,
            double r)
            => GmlNative.KDTree_CylinderIntersects(tree, x0, y0, z0, x1, y1, z1, r) != 0;

        public static bool KDTreeConeIntersects(IntPtr tree,
            double x0, double y0, double z0,
            double x1, double y1, double z1,
            double r)
            => GmlNative.KDTree_ConeIntersects(tree, x0, y0, z0, x1, y1, z1, r) != 0;
    }
}
