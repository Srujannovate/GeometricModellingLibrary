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
