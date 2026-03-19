#include "pch.h"
#include "Include/KDTree.h"
#include "Include/KDTree.tpp"

// Explicitly instantiate the template for K=3, T=double so clients only need the declarations.
template class gml::KDTree<3, double>;
