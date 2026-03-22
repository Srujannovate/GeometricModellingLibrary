#include "pch.h"
#include <cmath>
#include "Include/GMLLibrary.h"
#include "Include/KDTree.h"

extern "C" GMLLIBRARY_API int GML_Add(int a, int b) {
    return a + b;
}

extern "C" GMLLIBRARY_API int GML_SpheresIntersect(
    double x1, double y1, double z1, double r1,
    double x2, double y2, double z2, double r2) {
    // Clamp radii to be non-negative to avoid surprising results
    r1 = r1 < 0.0 ? 0.0 : r1;
    r2 = r2 < 0.0 ? 0.0 : r2;
    const double dx = x1 - x2;
    const double dy = y1 - y2;
    const double dz = z1 - z2;
    const double dist2 = dx * dx + dy * dy + dz * dz;
    const double sumR = r1 + r2;
    return dist2 <= (sumR * sumR) ? 1 : 0;
}

extern "C" GMLLIBRARY_API int GML_KDTree_SphereIntersects(
    const gml::KDTree3d* tree,
    double cx, double cy, double cz,
    double r) {
    if (!tree) return 0;
    if (r < 0.0) r = 0.0;
    const gml::KDTree3d::Point q{ cx, cy, cz };
    auto nearest = tree->nearest(q);
    if (!nearest) return 0;
    return (nearest->distance <= r) ? 1 : 0;
}
