#include "pch.h"
#include <cmath>
#include "Include/GMLLibrary.h"
#include "Include/KDTree.h"
#include <algorithm>
#include <cmath>

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

static inline double dot3(double ax,double ay,double az,double bx,double by,double bz){
    return ax*bx + ay*by + az*bz;
}

extern "C" GMLLIBRARY_API int GML_KDTree_CylinderIntersects(
    const gml::KDTree3d* tree,
    double x0, double y0, double z0,
    double x1, double y1, double z1,
    double r) {
    if (!tree) return 0;
    if (r <= 0.0) return 0;

    // Axis vector and length
    const double vx = x1 - x0, vy = y1 - y0, vz = z1 - z0;
    const double L2 = vx*vx + vy*vy + vz*vz;
    if (L2 <= 0.0) {
        // Degenerate to a sphere check at P0
        const gml::KDTree3d::Point q{ x0, y0, z0 };
        auto nearest = tree->nearest(q);
        return (nearest && nearest->distance <= r) ? 1 : 0;
    }
    const double L = std::sqrt(L2);
    const double nx = vx / L, ny = vy / L, nz = vz / L;

    // Step along the axis with stride <= 2r to cover the cylinder volume by overlapping spheres of radius r.
    const double step = std::max(1e-3, std::min(L, r)); // use r; clamp to avoid too-small values
    const int N = static_cast<int>(std::floor(L / step)) + 1;
    const double r2 = r * r;

    for (int i = 0; i <= N; ++i) {
        const double t = std::min(L, i * step);
        const double cx = x0 + nx * t;
        const double cy = y0 + ny * t;
        const double cz = z0 + nz * t;
        const gml::KDTree3d::Point center{ cx, cy, cz };
        auto items = tree->radiusSearch(center, r);
        for (const auto& it : items) {
            // Exact cylinder test: project to axis segment and check radial distance <= r, 0<=tau<=L
            const double px = it.point[0] - x0;
            const double py = it.point[1] - y0;
            const double pz = it.point[2] - z0;
            double tau = (px*vx + py*vy + pz*vz) / L2; // normalized [0,1] along segment
            if (tau < 0.0 || tau > 1.0) continue; // outside the capped cylinder
            const double qx = px - tau * vx;
            const double qy = py - tau * vy;
            const double qz = pz - tau * vz;
            const double d2 = qx*qx + qy*qy + qz*qz;
            if (d2 <= r2) return 1;
        }
    }
    return 0;
}

extern "C" GMLLIBRARY_API int GML_KDTree_ConeIntersects(
    const gml::KDTree3d* tree,
    double x0, double y0, double z0,
    double x1, double y1, double z1,
    double r) {
    if (!tree) return 0;
    if (r < 0.0) r = 0.0;
    const double vx = x1 - x0, vy = y1 - y0, vz = z1 - z0;
    const double L2 = vx*vx + vy*vy + vz*vz;
    if (L2 <= 0.0) {
        // Degenerate: treat as sphere at apex with radius r (if r==0, likely no hit)
        const gml::KDTree3d::Point q{ x0, y0, z0 };
        auto nearest = tree->nearest(q);
        return (nearest && nearest->distance <= r) ? 1 : 0;
    }
    const double L = std::sqrt(L2);
    // Loose bounding sphere centered at midpoint that encloses the whole cone
    const double cx = 0.5*(x0 + x1);
    const double cy = 0.5*(y0 + y1);
    const double cz = 0.5*(z0 + z1);
    const double Rbound = std::sqrt( (0.5*L)*(0.5*L) + r*r );
    const gml::KDTree3d::Point center{ cx, cy, cz };
    auto items = tree->radiusSearch(center, Rbound);
    if (items.empty()) return 0;

    // Exact cone test for candidates
    for (const auto& it : items) {
        const double px = it.point[0] - x0;
        const double py = it.point[1] - y0;
        const double pz = it.point[2] - z0;
        // Position along axis normalized to [0,1]
        const double tau = (px*vx + py*vy + pz*vz) / L2;
        if (tau < 0.0 || tau > 1.0) continue; // outside apex->base segment (flat base cap at tau=1)
        // Perpendicular distance squared to axis
        const double qx = px - tau*vx;
        const double qy = py - tau*vy;
        const double qz = pz - tau*vz;
        const double d2 = qx*qx + qy*qy + qz*qz;
        const double rt = tau * r; // local radius at this height
        if (d2 <= rt*rt) return 1;
    }
    return 0;
}
