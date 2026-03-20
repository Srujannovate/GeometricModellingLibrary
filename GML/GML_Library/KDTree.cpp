#include "pch.h"
#include <queue>
#include <algorithm>
#include <cmath>
#include <limits>
#include <utility>
#include "Include/KDTree.h"


// Explicitly instantiate the template for K=3, T=double so clients only need the declarations.

namespace gml {

    template <std::size_t K, class T>
    KDTree<K, T>::KDTree() = default;

    template <std::size_t K, class T>
    KDTree<K, T>::KDTree(std::vector<Item> items) {
        build(std::move(items));
    }

    template <std::size_t K, class T>
    void KDTree<K, T>::build(std::vector<Item> items) {
        clear();
        if (items.empty()) return;
        nodes_.reserve(items.size());
        root_ = buildRecursive(items.begin(), items.end(), /*axis=*/0);
        size_ = items.size();
    }

    template <std::size_t K, class T>
    void KDTree<K, T>::clear() {
        nodes_.clear();
        root_ = nullptr;
        size_ = 0;
    }

    template <std::size_t K, class T>
    std::size_t KDTree<K, T>::size() const noexcept { return size_; }

    template <std::size_t K, class T>
    bool KDTree<K, T>::empty() const noexcept { return size_ == 0; }

    template <std::size_t K, class T>
    void KDTree<K, T>::insert(const Point& p, const T& v) {
        auto* n = newNode({ p, v }, /*axis=*/0);
        if (!root_) {
            root_ = n;
            size_ = 1;
            return;
        }
        Node* cur = root_;
        std::size_t axis = 0;
        while (true) {
            if (p[axis] < cur->item.point[axis]) {
                if (cur->left) {
                    cur = cur->left;
                    axis = (axis + 1) % K;
                }
                else {
                    n->axis = (cur->axis + 1) % K;
                    cur->left = n;
                    break;
                }
            }
            else {
                if (cur->right) {
                    cur = cur->right;
                    axis = (axis + 1) % K;
                }
                else {
                    n->axis = (cur->axis + 1) % K;
                    cur->right = n;
                    break;
                }
            }
        }
        ++size_;
    }

    template <std::size_t K, class T>
    auto KDTree<K, T>::nearest(const Point& query) const -> std::optional<NearestResult> {
        if (!root_) return std::nullopt;
        NearestResult best;
        best.distance = std::numeric_limits<double>::infinity();
        nearestRecursive(root_, query, best);
        return best;
    }

    template <std::size_t K, class T>
    auto KDTree<K, T>::kNearest(const Point& query, std::size_t k) const -> std::vector<NearestResult> {
        std::vector<NearestResult> out;
        if (!root_ || k == 0) return out;
        using HeapItem = std::pair<double, const Node*>; // (negDist2, node)
        std::priority_queue<HeapItem> heap; // max-heap by -dist2
        kNearestRecursive(root_, query, k, heap);
        out.reserve(heap.size());
        while (!heap.empty()) {
            const auto [negd2, node] = heap.top();
            heap.pop();
            out.push_back(NearestResult{ node->item, std::sqrt(-negd2) });
        }
        std::reverse(out.begin(), out.end());
        return out;
    }

    template <std::size_t K, class T>
    auto KDTree<K, T>::radiusSearch(const Point& query, double r) const -> std::vector<Item> {
        std::vector<Item> out;
        if (!root_) return out;
        const double r2 = r * r;
        radiusRecursive(root_, query, r2, out);
        return out;
    }

    template <std::size_t K, class T>
    double KDTree<K, T>::dist2(const Point& a, const Point& b) {
        double d = 0.0;
        for (std::size_t i = 0; i < K; ++i) {
            const double di = a[i] - b[i];
            d += di * di;
        }
        return d;
    }

    template <std::size_t K, class T>
    typename KDTree<K, T>::Node* KDTree<K, T>::newNode(const Item& it, std::size_t axis) {
        nodes_.push_back(Node{ it, axis, nullptr, nullptr });
        return &nodes_.back();
    }

    template <std::size_t K, class T>
    template <class Iter>
    typename KDTree<K, T>::Node* KDTree<K, T>::buildRecursive(Iter first, Iter last, std::size_t axis) {
        const auto n = static_cast<std::size_t>(std::distance(first, last));
        if (n == 0) return nullptr;
        Iter mid = first + static_cast<std::ptrdiff_t>(n / 2);
        std::nth_element(first, mid, last, [axis](const Item& a, const Item& b) {
            return a.point[axis] < b.point[axis];
            });
        Node* node = newNode(*mid, axis);
        const std::size_t next = (axis + 1) % K;
        node->left = buildRecursive(first, mid, next);
        node->right = buildRecursive(mid + 1, last, next);
        return node;
    }

    template <std::size_t K, class T>
    void KDTree<K, T>::nearestRecursive(const Node* node, const Point& q, NearestResult& best) {
        if (!node) return;
        const double d2 = dist2(node->item.point, q);
        if (d2 < best.distance * best.distance) {
            best.item = node->item;
            best.distance = std::sqrt(d2);
        }
        const std::size_t axis = node->axis;
        const double diff = q[axis] - node->item.point[axis];
        const Node* first = diff < 0 ? node->left : node->right;
        const Node* second = diff < 0 ? node->right : node->left;
        nearestRecursive(first, q, best);
        if (diff * diff <= best.distance * best.distance) {
            nearestRecursive(second, q, best);
        }
    }

    template <std::size_t K, class T>
    void KDTree<K, T>::kNearestRecursive(const Node* node, const Point& q, std::size_t k,
        std::priority_queue<std::pair<double, const Node*>>& heap) {
        if (!node) return;
        const double d2 = dist2(node->item.point, q);
        const double negd2 = -d2;
        if (heap.size() < k) {
            heap.emplace(negd2, node);
        }
        else if (negd2 > heap.top().first) {
            heap.pop();
            heap.emplace(negd2, node);
        }
        const std::size_t axis = node->axis;
        const double diff = q[axis] - node->item.point[axis];
        const Node* first = diff < 0 ? node->left : node->right;
        const Node* second = diff < 0 ? node->right : node->left;
        kNearestRecursive(first, q, k, heap);
        double worstd2 = heap.empty() ? std::numeric_limits<double>::infinity() : -heap.top().first;
        if (diff * diff <= worstd2 || heap.size() < k) {
            kNearestRecursive(second, q, k, heap);
        }
    }

    template <std::size_t K, class T>
    void KDTree<K, T>::radiusRecursive(const Node* node, const Point& q, double r2, std::vector<Item>& out) {
        if (!node) return;
        const double d2 = dist2(node->item.point, q);
        if (d2 <= r2) out.push_back(node->item);
        const std::size_t axis = node->axis;
        const double diff = q[axis] - node->item.point[axis];
        if (diff <= 0) {
            radiusRecursive(node->left, q, r2, out);
            if (diff * diff <= r2) radiusRecursive(node->right, q, r2, out);
        }
        else {
            radiusRecursive(node->right, q, r2, out);
            if (diff * diff <= r2) radiusRecursive(node->left, q, r2, out);
        }
    }

} // namespace gml

template class __declspec(dllexport) gml::KDTree<3, double>;
