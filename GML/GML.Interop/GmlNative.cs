using System;
using System.Runtime.InteropServices;

namespace GML.Interop
{
    internal static class GmlNative
    {
        private const string Dll = "GML_Library.dll";

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true, EntryPoint = "GML_Add")]
        public static extern int Add(int a, int b);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true, EntryPoint = "GML_SpheresIntersect")]
        public static extern int SpheresIntersect(
            double x1, double y1, double z1, double r1,
            double x2, double y2, double z2, double r2);

        // KDTree-related APIs. The native side expects a pointer to gml::KDTree3d.
        // Without constructor/destructor exports, consumers need to obtain such a pointer from native code.
        // We expose the signatures with IntPtr for future use.
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true, EntryPoint = "GML_KDTree_SphereIntersects")]
        public static extern int KDTree_SphereIntersects(
            IntPtr tree,
            double cx, double cy, double cz,
            double r);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true, EntryPoint = "GML_KDTree_CylinderIntersects")]
        public static extern int KDTree_CylinderIntersects(
            IntPtr tree,
            double x0, double y0, double z0,
            double x1, double y1, double z1,
            double r);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true, EntryPoint = "GML_KDTree_ConeIntersects")]
        public static extern int KDTree_ConeIntersects(
            IntPtr tree,
            double x0, double y0, double z0,
            double x1, double y1, double z1,
            double r);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true, EntryPoint = "GML_KDTree_CreateFromXYZ")]
        public static extern IntPtr KDTree_CreateFromXYZ([In] double[] xyz, UIntPtr count);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true, EntryPoint = "GML_KDTree_Destroy")]
        public static extern void KDTree_Destroy(IntPtr tree);
    }
}
