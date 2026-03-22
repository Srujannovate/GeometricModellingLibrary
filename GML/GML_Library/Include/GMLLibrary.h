#pragma once

#include <cstddef> // for std::size_t

#ifdef GMLLIBRARY_EXPORTS
#define GMLLIBRARY_API __declspec(dllexport)
#else
#define GMLLIBRARY_API __declspec(dllimport)
#endif

// Forward decl for KDTree3d so callers can pass a pointer without including KDTree.h before this header
namespace gml {
    template <std::size_t K, class T> class KDTree; // forward declaration only
    using KDTree3d = KDTree<3, double>;
}

// Example exported function to ensure an import library (.lib) is generated
extern "C" GMLLIBRARY_API int GML_Add(int a, int b);

// Returns 1 if the spheres intersect/touch, 0 otherwise.
// (x1,y1,z1,r1): center and radius of the first sphere
// (x2,y2,z2,r2): center and radius of the second sphere
extern "C" GMLLIBRARY_API int GML_SpheresIntersect(
    double x1, double y1, double z1, double r1,
    double x2, double y2, double z2, double r2);

// KDTree vs sphere collision: returns 1 if any point in the KDTree lies within radius r of (cx,cy,cz), else 0.
extern "C" GMLLIBRARY_API int GML_KDTree_SphereIntersects(
    const gml::KDTree3d* tree,
    double cx, double cy, double cz,
    double r);

// KDTree vs finite cylinder collision.
// Cylinder defined by segment [P0(x0,y0,z0), P1(x1,y1,z1)] as the axis and radius r (flat caps at P0 and P1).
// Returns 1 if any KDTree point lies strictly inside or on the cylinder, else 0.
extern "C" GMLLIBRARY_API int GML_KDTree_CylinderIntersects(
    const gml::KDTree3d* tree,
    double x0, double y0, double z0,
    double x1, double y1, double z1,
    double r);

// KDTree vs finite right circular cone collision.
// Cone defined by apex P0(x0,y0,z0) and base-center P1(x1,y1,z1) with base radius r.
// Radius grows linearly from 0 at P0 to r at P1 (flat base cap at P1). Returns 1 if any KDTree point is inside/on the cone.
extern "C" GMLLIBRARY_API int GML_KDTree_ConeIntersects(
    const gml::KDTree3d* tree,
    double x0, double y0, double z0,
    double x1, double y1, double z1,
    double r);
