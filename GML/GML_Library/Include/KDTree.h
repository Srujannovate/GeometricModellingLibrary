#pragma once

#include <array>
#include <vector>
#include <optional>
#include <cstddef>
#include <limits>
#include <queue>
#include <utility>

// KDTree declarations. Template definitions are in KDTree.tpp and must be available to translation units.
// If you need a true .cpp separation, provide explicit instantiations for the K,T pairs you use.

namespace gml {

template <std::size_t K, class T>
class KDTree {
public:
    static_assert(K > 0, "K must be > 0");

    using Point = std::array<double, K>;

    struct Item {
        Point point{};
        T value{};
    };

    struct NearestResult {
        Item item{};
        double distance = std::numeric_limits<double>::infinity(); // Euclidean distance
    };

    KDTree();
    explicit KDTree(std::vector<Item> items);

    void build(std::vector<Item> items);
    void clear();

    std::size_t size() const noexcept;
    bool empty() const noexcept;

    void insert(const Point& p, const T& v);

    std::optional<NearestResult> nearest(const Point& query) const;
    std::vector<NearestResult> kNearest(const Point& query, std::size_t k) const;
    std::vector<Item> radiusSearch(const Point& query, double r) const;

private:
    struct Node;

    // Storage for all nodes so their addresses remain stable and are cleaned up automatically
    std::vector<Node> nodes_;
    Node* root_ = nullptr;
    std::size_t size_ = 0;

    static double dist2(const Point& a, const Point& b);
    Node* newNode(const Item& it, std::size_t axis);

    template <class Iter>
    Node* buildRecursive(Iter first, Iter last, std::size_t axis);

    static void nearestRecursive(const Node* node, const Point& q, NearestResult& best);
    static void kNearestRecursive(const Node* node, const Point& q, std::size_t k,
                                  std::priority_queue<std::pair<double, const Node*>>& heap);
    static void radiusRecursive(const Node* node, const Point& q, double r2, std::vector<Item>& out);
};

// Explicit instantiation provided by the library for common usage:
extern template class KDTree<3, double>;
using KDTree3d = KDTree<3, double>;

} // namespace gml
