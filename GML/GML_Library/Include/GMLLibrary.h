#pragma once

#ifdef GMLLIBRARY_EXPORTS
#define GMLLIBRARY_API __declspec(dllexport)
#else
#define GMLLIBRARY_API __declspec(dllimport)
#endif

// Example exported function to ensure an import library (.lib) is generated
extern "C" GMLLIBRARY_API int GML_Add(int a, int b);
