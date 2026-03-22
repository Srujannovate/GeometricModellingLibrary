// GML.cpp : Defines the entry point for the application.
//

#include "framework.h"
#include "GML.h"
#include "GMLLibrary.h"
#include "KDTree.h"
#include <vector>
#include <string>
#include <sstream>
#include <fstream>
#include <filesystem>
#include <iomanip>
#include <optional>
#include <chrono>
#include <algorithm>
#include <cmath>
#include <commdlg.h>
#pragma comment(lib, "Comdlg32.lib")

#define MAX_LOADSTRING 100

// Global Variables:
HINSTANCE hInst;                                // current instance
WCHAR szTitle[MAX_LOADSTRING];                  // The title bar text
WCHAR szWindowClass[MAX_LOADSTRING];            // the main window class name

// Forward declarations of functions included in this code module:
ATOM                MyRegisterClass(HINSTANCE hInstance);
BOOL                InitInstance(HINSTANCE, int);
LRESULT CALLBACK    WndProc(HWND, UINT, WPARAM, LPARAM);
INT_PTR CALLBACK    About(HWND, UINT, WPARAM, LPARAM);
INT_PTR CALLBACK    AddNumbersDlgProc(HWND, UINT, WPARAM, LPARAM);

// OBJ geometry storage and helpers
struct Point3 { double x = 0, y = 0, z = 0; };
struct Triangle { int i0 = -1, i1 = -1, i2 = -1; };
static std::vector<Point3> g_points;            // loaded 3D points
static std::vector<Triangle> g_tris;            // loaded triangles (indices into g_points)
static std::vector<std::vector<int>> g_vertexToTris; // adjacency: vertex -> triangle indices
static double g_maxEdgeLen = 0.0;               // global max edge length for conservative KD culling
static std::wstring g_summary;                  // text rendered in the main window
static gml::KDTree3d g_kdtree;                  // KD-tree built from g_points

// Simple model transform stored alongside the KDTree (row-major 4x4; last row [0 0 0 1])
struct Transform4x4 {
    double m[16];   // model->world
    double inv[16]; // world->model
};
static Transform4x4 g_modelXf; // identity by default (initialized in code below)

// Forward declarations of transform helpers (defined later in this file)
static void   XfSetIdentity(Transform4x4& t);
static void   XfSetTranslationUniformScale(Transform4x4& t, double tx, double ty, double tz, double s);
static void   XfSetTranslationRotationUniformScaleEuler(Transform4x4& t, double tx, double ty, double tz,
    double rx_deg, double ry_deg, double rz_deg, double s);
static Point3 XfApplyPointRowMajor(const double m[16], const Point3& p);
static double XfMaxColumnNorm3x3(const double m[16]);

// Forward declarations for dialogs used in menu handlers
struct TransformParams { double tx, ty, tz, yawZ, pitchY, rollX, s; };
static bool ShowTransformDialog(HWND owner, TransformParams& out);
struct SphereParams { double cx, cy, cz, r; };
static bool ShowSphereDialog(HWND owner, SphereParams& out);

static void UpdateSummaryText();
static void BuildKDTreeFromPoints();
static bool LoadOBJ(const std::filesystem::path& path,
    std::vector<Point3>& outPts,
    std::vector<Triangle>& outTris,
    std::wstring& error);
static void BuildAdjacencyAndEdgeStats();
static bool SphereIntersectsMeshKDRefined(double cx, double cy, double cz, double r);
static std::optional<std::filesystem::path> ShowOpenObjDialog(HWND owner);
static std::optional<std::filesystem::path> ShowSaveTxtDialog(HWND owner);
static bool SaveTransformedPointsTo(const std::filesystem::path& path);

// Simple single-input modal dialog (built at runtime) to capture a double value.
static bool PromptDouble(HWND owner, const wchar_t* title, const wchar_t* prompt, double& out);

int APIENTRY wWinMain(_In_ HINSTANCE hInstance,
    _In_opt_ HINSTANCE hPrevInstance,
    _In_ LPWSTR    lpCmdLine,
    _In_ int       nCmdShow)
{
    UNREFERENCED_PARAMETER(hPrevInstance);
    UNREFERENCED_PARAMETER(lpCmdLine);

    // TODO: Place code here.

    // Initialize global strings
    LoadStringW(hInstance, IDS_APP_TITLE, szTitle, MAX_LOADSTRING);
    LoadStringW(hInstance, IDC_GML, szWindowClass, MAX_LOADSTRING);
    MyRegisterClass(hInstance);

    // Perform application initialization:
    if (!InitInstance(hInstance, nCmdShow))
    {
        return FALSE;
    }

    HACCEL hAccelTable = LoadAccelerators(hInstance, MAKEINTRESOURCE(IDC_GML));

    MSG msg;

    // Main message loop:
    while (GetMessage(&msg, nullptr, 0, 0))
    {
        if (!TranslateAccelerator(msg.hwnd, hAccelTable, &msg))
        {
            TranslateMessage(&msg);
            DispatchMessage(&msg);
        }
    }

    return (int)msg.wParam;
}



//
//  FUNCTION: MyRegisterClass()
//
//  PURPOSE: Registers the window class.
//
ATOM MyRegisterClass(HINSTANCE hInstance)
{
    WNDCLASSEXW wcex;

    wcex.cbSize = sizeof(WNDCLASSEX);

    wcex.style = CS_HREDRAW | CS_VREDRAW;
    wcex.lpfnWndProc = WndProc;
    wcex.cbClsExtra = 0;
    wcex.cbWndExtra = 0;
    wcex.hInstance = hInstance;
    wcex.hIcon = LoadIcon(hInstance, MAKEINTRESOURCE(IDI_GML));
    wcex.hCursor = LoadCursor(nullptr, IDC_ARROW);
    wcex.hbrBackground = (HBRUSH)(COLOR_WINDOW + 1);
    wcex.lpszMenuName = MAKEINTRESOURCEW(IDC_GML);
    wcex.lpszClassName = szWindowClass;
    wcex.hIconSm = LoadIcon(wcex.hInstance, MAKEINTRESOURCE(IDI_SMALL));

    return RegisterClassExW(&wcex);
}

//
//   FUNCTION: InitInstance(HINSTANCE, int)
//
//   PURPOSE: Saves instance handle and creates main window
//
//   COMMENTS:
//
//        In this function, we save the instance handle in a global variable and
//        create and display the main program window.
//
BOOL InitInstance(HINSTANCE hInstance, int nCmdShow)
{
    hInst = hInstance; // Store instance handle in our global variable

    HWND hWnd = CreateWindowW(szWindowClass, szTitle, WS_OVERLAPPEDWINDOW,
        CW_USEDEFAULT, 0, CW_USEDEFAULT, 0, nullptr, nullptr, hInstance, nullptr);

    if (!hWnd)
    {
        return FALSE;
    }

    // Insert a "Load OBJ..." menu item at runtime to avoid editing resources.
    if (HMENU hMenu = GetMenu(hWnd))
    {
        // Build a File menu and move Load OBJ under it; insert at position 0 so it appears first
        HMENU hModel = CreatePopupMenu();
        AppendMenuW(hModel, MF_STRING, IDM_LOADOBJ, L"&Load OBJ...");
        AppendMenuW(hModel, MF_STRING, IDM_EXPORT_TRANSFORMED, L"&Export Transformed Points...");
        InsertMenuW(hMenu, 1, MF_BYPOSITION | MF_POPUP, (UINT_PTR)hModel, L"&Model");

        // Build an Edit menu for queries/intersections/transforms; insert at position 1
        HMENU hEdit = CreatePopupMenu();
        AppendMenuW(hEdit, MF_STRING, IDM_FINDNEAREST, L"Find &Nearest Point...");
        AppendMenuW(hEdit, MF_STRING, IDM_SPHEREMESH, L"Sphere - Mesh &Intersect...");
        AppendMenuW(hEdit, MF_STRING, IDM_SETTRANSFORM, L"Set &Transform (T,R,S)...");
        InsertMenuW(hMenu, 2, MF_BYPOSITION | MF_POPUP, (UINT_PTR)hEdit, L"&Edit");

        DrawMenuBar(hWnd);
    }

    ShowWindow(hWnd, nCmdShow);
    UpdateWindow(hWnd);

    // Initialize transform to identity on startup
    XfSetIdentity(g_modelXf);

    return TRUE;
}

//
//  FUNCTION: WndProc(HWND, UINT, WPARAM, LPARAM)
//
//  PURPOSE: Processes messages for the main window.
//
//  WM_COMMAND  - process the application menu
//  WM_PAINT    - Paint the main window
//  WM_DESTROY  - post a quit message and return
//
//
LRESULT CALLBACK WndProc(HWND hWnd, UINT message, WPARAM wParam, LPARAM lParam)
{
    switch (message)
    {
    case WM_COMMAND:
    {
        int wmId = LOWORD(wParam);
        // Parse the menu selections:
        switch (wmId)
        {
        case IDM_ABOUT:
            DialogBox(hInst, MAKEINTRESOURCE(IDD_ABOUTBOX), hWnd, About);
            break;
        case IDM_ADDNUM:
            DialogBox(hInst, MAKEINTRESOURCE(IDD_ADDNUM), hWnd, AddNumbersDlgProc);
            break;
        case IDM_LOADOBJ:
        {
            auto sel = ShowOpenObjDialog(hWnd);
            if (sel)
            {
                std::wstring error;
                std::vector<Point3> tmpPts;
                std::vector<Triangle> tmpTris;
                if (LoadOBJ(*sel, tmpPts, tmpTris, error))
                {
                    g_points = std::move(tmpPts);
                    g_tris = std::move(tmpTris);
                    BuildAdjacencyAndEdgeStats();
                    BuildKDTreeFromPoints();
                    UpdateSummaryText();
                    InvalidateRect(hWnd, nullptr, TRUE);
                }
                else
                {
                    MessageBoxW(hWnd, error.c_str(), L"Failed to load OBJ", MB_OK | MB_ICONERROR);
                }
            }
        }
        break;
        case IDM_FINDNEAREST:
        {
            if (g_kdtree.empty()) {
                MessageBoxW(hWnd, L"No points loaded. Use 'Load OBJ...' first.", L"KDTree", MB_OK | MB_ICONINFORMATION);
                break;
            }
            double x = 0, y = 0, z = 0;
            if (!PromptDouble(hWnd, L"Nearest Point", L"Enter X:", x)) break;
            if (!PromptDouble(hWnd, L"Nearest Point", L"Enter Y:", y)) break;
            if (!PromptDouble(hWnd, L"Nearest Point", L"Enter Z:", z)) break;
            Point3 qw{ x, y, z };
            Point3 ql = XfApplyPointRowMajor(g_modelXf.inv, qw); // world->model
            gml::KDTree3d::Point q{ ql.x, ql.y, ql.z };
            auto t0 = std::chrono::steady_clock::now();
            auto res = g_kdtree.nearest(q);
            auto t1 = std::chrono::steady_clock::now();
            const auto us = std::chrono::duration_cast<std::chrono::microseconds>(t1 - t0).count();
            if (!res) {
                MessageBoxW(hWnd, L"KDTree query failed (no data).", L"KDTree", MB_OK | MB_ICONWARNING);
                break;
            }
            const auto& it = res->item;
            const double dist = res->distance;
            int idx = static_cast<int>(std::llround(it.value));
            std::wostringstream oss;
            oss.setf(std::ios::fixed, std::ios::floatfield);
            oss << std::setprecision(6)
                << L"Query: (" << x << L", " << y << L", " << z << L")\r\n"
                << L"Nearest index: " << idx << L" at (" << it.point[0] << L", " << it.point[1] << L", " << it.point[2] << L")\r\n"
                << L"Distance: " << dist << L"\r\n"
                << L"Compute time: " << us << L" microseconds";
            MessageBoxW(hWnd, oss.str().c_str(), L"Nearest Point Result", MB_OK | MB_ICONINFORMATION);
            // Prepend to summary and repaint
            g_summary = oss.str() + L"\r\n\r\n" + g_summary;
            InvalidateRect(hWnd, nullptr, TRUE);
        }
        break;
            case IDM_SPHEREMESH:
            {
                if (g_kdtree.empty()) {
                    MessageBoxW(hWnd, L"No mesh loaded. Use 'Load OBJ...' first.", L"Sphere-Mesh", MB_OK | MB_ICONINFORMATION);
                    break;
                }
                SphereParams sp{0,0,0,1};
                if (!ShowSphereDialog(hWnd, sp)) break;
                auto t0 = std::chrono::steady_clock::now();
                const bool hit = SphereIntersectsMeshKDRefined(sp.cx, sp.cy, sp.cz, sp.r);
            auto t1 = std::chrono::steady_clock::now();
            const auto us = std::chrono::duration_cast<std::chrono::microseconds>(t1 - t0).count();
            std::wostringstream oss;
            oss.setf(std::ios::fixed, std::ios::floatfield);
            oss << (hit ? L"INTERSECT" : L"DISJOINT") << L"\r\nTime: " << us << L" us\r\n"
                << L"Vertices: " << g_points.size() << L", Triangles: " << g_tris.size();
            MessageBoxW(hWnd, oss.str().c_str(), L"Sphere - Mesh Result", MB_OK | MB_ICONINFORMATION);
            g_summary = oss.str() + L"\r\n\r\n" + g_summary;
            InvalidateRect(hWnd, nullptr, TRUE);
        }
        break;
            case IDM_SETTRANSFORM:
            {
                TransformParams p{0,0,0,0,0,0,1};
                if (!ShowTransformDialog(hWnd, p)) break;
                XfSetTranslationRotationUniformScaleEuler(g_modelXf, p.tx, p.ty, p.tz, p.rollX, p.pitchY, p.yawZ, p.s);
                UpdateSummaryText();
                InvalidateRect(hWnd, nullptr, TRUE);
            }
                break;
            case IDM_EXPORT_TRANSFORMED:
            {
                if (g_points.empty()) {
                    MessageBoxW(hWnd, L"No model loaded. Use Load OBJ first.", L"Export", MB_OK | MB_ICONINFORMATION);
                    break;
                }
                auto savePath = ShowSaveTxtDialog(hWnd);
                if (!savePath) break;
                if (!SaveTransformedPointsTo(*savePath)) {
                    MessageBoxW(hWnd, L"Failed to write file.", L"Export", MB_OK | MB_ICONERROR);
                } else {
                    MessageBoxW(hWnd, L"Transformed points exported successfully.", L"Export", MB_OK | MB_ICONINFORMATION);
                }
            }
                break;
            case IDM_EXIT:
            DestroyWindow(hWnd);
            break;
        default:
            return DefWindowProc(hWnd, message, wParam, lParam);
        }
    }
    break;
    case WM_PAINT:
    {
        PAINTSTRUCT ps;
        HDC hdc = BeginPaint(hWnd, &ps);
        // Render summary text with the loaded point count and coordinates.
        RECT rc{};
        GetClientRect(hWnd, &rc);
        DrawTextW(hdc,
            g_summary.c_str(),
            static_cast<int>(g_summary.size()),
            &rc,
            DT_LEFT | DT_TOP | DT_NOPREFIX | DT_WORDBREAK);
        EndPaint(hWnd, &ps);
    }
    break;
    case WM_DESTROY:
        PostQuitMessage(0);
        break;
    default:
        return DefWindowProc(hWnd, message, wParam, lParam);
    }
    return 0;
}

// Message handler for about box.
INT_PTR CALLBACK About(HWND hDlg, UINT message, WPARAM wParam, LPARAM lParam)
{
    UNREFERENCED_PARAMETER(lParam);
    switch (message)
    {
    case WM_INITDIALOG:
        return (INT_PTR)TRUE;

    case WM_COMMAND:
        if (LOWORD(wParam) == IDOK || LOWORD(wParam) == IDCANCEL)
        {
            EndDialog(hDlg, LOWORD(wParam));
            return (INT_PTR)TRUE;
        }
        break;
    }
    return (INT_PTR)FALSE;
}

// Add Numbers dialog procedure
INT_PTR CALLBACK AddNumbersDlgProc(HWND hDlg, UINT message, WPARAM wParam, LPARAM lParam)
{
    UNREFERENCED_PARAMETER(lParam);
    switch (message)
    {
    case WM_INITDIALOG:
        return (INT_PTR)TRUE;
    case WM_COMMAND:
        switch (LOWORD(wParam))
        {
        case IDOK:
        {
            wchar_t bufA[64] = { 0 }, bufB[64] = { 0 };
            GetDlgItemTextW(hDlg, IDC_EDIT_A, bufA, 63);
            GetDlgItemTextW(hDlg, IDC_EDIT_B, bufB, 63);
            wchar_t* endA = nullptr; wchar_t* endB = nullptr;
            long a = wcstol(bufA, &endA, 10);
            long b = wcstol(bufB, &endB, 10);
            int sum = GML_Add((int)a, (int)b);
            wchar_t out[128];
            swprintf_s(out, L"%ld + %ld = %d", a, b, sum);
            MessageBoxW(GetParent(hDlg), out, L"Sum", MB_OK | MB_ICONINFORMATION);
            EndDialog(hDlg, IDOK);
            return (INT_PTR)TRUE;
        }
        case IDCANCEL:
            EndDialog(hDlg, IDCANCEL);
            return (INT_PTR)TRUE;
        }
        break;
    }
    return (INT_PTR)FALSE;
}

// Build a printable summary of the loaded points.
static void UpdateSummaryText()
{
    std::wostringstream oss;
    oss.setf(std::ios::fixed, std::ios::floatfield);
    oss << L"Points loaded: " << g_points.size() << L"\r\n";
    oss << L"Triangles loaded: " << g_tris.size() << L"\r\n";
    oss << L"Max edge length: " << std::setprecision(6) << g_maxEdgeLen << L"\r\n";
    oss << L"Transform (row-major): ["
        << g_modelXf.m[0] << L"," << g_modelXf.m[1] << L"," << g_modelXf.m[2] << L"," << g_modelXf.m[3] << L"; "
        << g_modelXf.m[4] << L"," << g_modelXf.m[5] << L"," << g_modelXf.m[6] << L"," << g_modelXf.m[7] << L"; "
        << g_modelXf.m[8] << L"," << g_modelXf.m[9] << L"," << g_modelXf.m[10] << L"," << g_modelXf.m[11] << L"; "
        << g_modelXf.m[12] << L"," << g_modelXf.m[13] << L"," << g_modelXf.m[14] << L"," << g_modelXf.m[15] << L"]\r\n";
    oss << L"Format: index: x y z\r\n\r\n";
    oss << std::setprecision(6);
    for (size_t i = 0; i < g_points.size(); ++i)
    {
        const auto& p = g_points[i];
        oss << i << L": " << p.x << L"\t" << p.y << L"\t" << p.z << L"\r\n";
    }
    g_summary = oss.str();
}

// Build KDTree from current g_points.
static void BuildKDTreeFromPoints()
{
    std::vector<gml::KDTree3d::Item> items;
    items.reserve(g_points.size());
    for (size_t i = 0; i < g_points.size(); ++i)
    {
        gml::KDTree3d::Point pt{ g_points[i].x, g_points[i].y, g_points[i].z };
        items.push_back(gml::KDTree3d::Item{ pt, static_cast<double>(i) });
    }
    g_kdtree.build(std::move(items));
}

// Show a file-open dialog filtered to .obj files.
static std::optional<std::filesystem::path> ShowOpenObjDialog(HWND owner)
{
    wchar_t fileBuf[MAX_PATH] = { 0 };
    OPENFILENAMEW ofn{};
    ofn.lStructSize = sizeof(ofn);
    ofn.hwndOwner = owner;
    ofn.lpstrFilter = L"Wavefront OBJ (*.obj)\0*.obj\0All Files (*.*)\0*.*\0\0";
    ofn.lpstrFile = fileBuf;
    ofn.nMaxFile = MAX_PATH;
    ofn.Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_EXPLORER;
    ofn.lpstrTitle = L"Select an OBJ file";
    if (GetOpenFileNameW(&ofn))
    {
        return std::filesystem::path{ fileBuf };
    }
    return std::nullopt;
}

static std::optional<std::filesystem::path> ShowSaveTxtDialog(HWND owner)
{
    wchar_t fileBuf[MAX_PATH] = {0};
    OPENFILENAMEW ofn{};
    ofn.lStructSize = sizeof(ofn);
    ofn.hwndOwner = owner;
    ofn.lpstrFilter = L"Text Files (*.txt)\0*.txt\0All Files (*.*)\0*.*\0\0";
    ofn.lpstrFile = fileBuf;
    ofn.nMaxFile = MAX_PATH;
    ofn.Flags = OFN_OVERWRITEPROMPT | OFN_EXPLORER;
    ofn.lpstrTitle = L"Export Transformed Points";
    ofn.lpstrDefExt = L"txt";
    if (GetSaveFileNameW(&ofn))
    {
        return std::filesystem::path{fileBuf};
    }
    return std::nullopt;
}

static bool SaveTransformedPointsTo(const std::filesystem::path& path)
{
    if (g_points.empty()) return false;
    std::ofstream out(path, std::ios::out | std::ios::trunc);
    if (!out.is_open()) return false;
    out.setf(std::ios::fixed, std::ios::floatfield);
    out << std::setprecision(9);
    for (const auto& p : g_points)
    {
        Point3 pw = XfApplyPointRowMajor(g_modelXf.m, p);
        out << pw.x << ' ' << pw.y << ' ' << pw.z << "\n";
    }
    return true;
}

// ---------- Transform helpers ----------
static void XfSetIdentity(Transform4x4& t)
{
    for (int i = 0; i < 16; ++i) t.m[i] = t.inv[i] = 0.0;
    t.m[0] = t.m[5] = t.m[10] = t.m[15] = 1.0;
    t.inv[0] = t.inv[5] = t.inv[10] = t.inv[15] = 1.0;
}
static void XfSetTranslationUniformScale(Transform4x4& t, double tx, double ty, double tz, double s)
{
    if (s == 0.0) s = 1.0;
    for (int i = 0; i < 16; ++i) t.m[i] = t.inv[i] = 0.0;
    // m = S then T: x_world = s*x + t
    t.m[0] = t.m[5] = t.m[10] = s; t.m[15] = 1.0; t.m[3] = tx; t.m[7] = ty; t.m[11] = tz;
    // inv: x_model = (x_world - t) / s
    const double is = 1.0 / s;
    t.inv[0] = t.inv[5] = t.inv[10] = is; t.inv[15] = 1.0; t.inv[3] = -tx * is; t.inv[7] = -ty * is; t.inv[11] = -tz * is;
}

static inline double Deg2Rad(double d) { return d * 3.14159265358979323846 / 180.0; }

// Set transform with translation T=(tx,ty,tz), Euler ZYX (yaw Z, pitch Y, roll X) in degrees, and uniform scale s.
// Row-major matrices; apply as x_world = (s * Rzyx) * x_model + T.
static void XfSetTranslationRotationUniformScaleEuler(Transform4x4& t, double tx, double ty, double tz,
    double rx_deg, double ry_deg, double rz_deg, double s)
{
    if (s == 0.0) s = 1.0;
    const double cx = std::cos(Deg2Rad(rx_deg)), sx = std::sin(Deg2Rad(rx_deg));
    const double cy = std::cos(Deg2Rad(ry_deg)), sy = std::sin(Deg2Rad(ry_deg));
    const double cz = std::cos(Deg2Rad(rz_deg)), sz = std::sin(Deg2Rad(rz_deg));
    // R = Rz * Ry * Rx (ZYX yaw-pitch-roll)
    const double r00 = cy * cz;
    const double r01 = cz * sx * sy - cx * sz;
    const double r02 = sx * sz + cx * cz * sy;
    const double r10 = cy * sz;
    const double r11 = cx * cz + sx * sy * sz;
    const double r12 = cx * sy * sz - cz * sx;
    const double r20 = -sy;
    const double r21 = cy * sx;
    const double r22 = cx * cy;

    const double s00 = s * r00, s01 = s * r01, s02 = s * r02;
    const double s10 = s * r10, s11 = s * r11, s12 = s * r12;
    const double s20 = s * r20, s21 = s * r21, s22 = s * r22;

    for (int i = 0; i < 16; ++i) t.m[i] = t.inv[i] = 0.0;
    t.m[0] = s00; t.m[1] = s01; t.m[2] = s02; t.m[3] = tx;
    t.m[4] = s10; t.m[5] = s11; t.m[6] = s12; t.m[7] = ty;
    t.m[8] = s20; t.m[9] = s21; t.m[10] = s22; t.m[11] = tz;
    t.m[15] = 1.0;

    const double is = 1.0 / s;
    // inv linear = (1/s) * R^T (since R is orthonormal)
    const double il00 = is * r00, il01 = is * r10, il02 = is * r20;
    const double il10 = is * r01, il11 = is * r11, il12 = is * r21;
    const double il20 = is * r02, il21 = is * r12, il22 = is * r22;
    t.inv[0] = il00; t.inv[1] = il10; t.inv[2] = il20;
    t.inv[4] = il01; t.inv[5] = il11; t.inv[6] = il21;
    t.inv[8] = il02; t.inv[9] = il12; t.inv[10] = il22;
    t.inv[15] = 1.0;
    // inv translation = -inv_linear * T
    t.inv[3] = -(il00 * tx + il01 * ty + il02 * tz);
    t.inv[7] = -(il10 * tx + il11 * ty + il12 * tz);
    t.inv[11] = -(il20 * tx + il21 * ty + il22 * tz);
}
static Point3 XfApplyPointRowMajor(const double m[16], const Point3& p)
{
    const double x = m[0] * p.x + m[1] * p.y + m[2] * p.z + m[3];
    const double y = m[4] * p.x + m[5] * p.y + m[6] * p.z + m[7];
    const double z = m[8] * p.x + m[9] * p.y + m[10] * p.z + m[11];
    // assume affine w=1
    return Point3{ x, y, z };
}
static double XfMaxColumnNorm3x3(const double m[16])
{
    // Column norms of the 3x3 linear part (row-major storage)
    const double c0 = std::sqrt(m[0] * m[0] + m[4] * m[4] + m[8] * m[8]);
    const double c1 = std::sqrt(m[1] * m[1] + m[5] * m[5] + m[9] * m[9]);
    const double c2 = std::sqrt(m[2] * m[2] + m[6] * m[6] + m[10] * m[10]);
    return std::max({ c0,c1,c2 });
}

// OBJ parser: loads vertex positions (v) and faces (f). Faces are triangulated via fan method.
static bool LoadOBJ(const std::filesystem::path& path,
    std::vector<Point3>& outPts,
    std::vector<Triangle>& outTris,
    std::wstring& error)
{
    outPts.clear();
    outTris.clear();
    std::ifstream in(path, std::ios::in);
    if (!in.is_open())
    {
        error = L"Could not open file: " + path.wstring();
        return false;
    }
    auto parse_index = [](const std::string& tok, size_t vcount) -> std::optional<int>
        {
            // Extract the vertex index before first '/'
            size_t slash = tok.find('/');
            std::string s = tok.substr(0, slash);
            if (s.empty()) return std::nullopt;
            char* endp = nullptr;
            long idx = strtol(s.c_str(), &endp, 10);
            if (endp == s.c_str()) return std::nullopt;
            // OBJ: positive indices are 1-based; negative are relative to end
            long actual = 0;
            if (idx > 0) actual = idx; else actual = (long)vcount + idx + 1;
            if (actual < 1 || actual >(long)vcount) return std::nullopt;
            return static_cast<int>(actual - 1); // to 0-based
        };

    std::string line;
    while (std::getline(in, line))
    {
        // trim leading spaces
        size_t i = 0; while (i < line.size() && (line[i] == ' ' || line[i] == '\t')) ++i; if (i >= line.size()) continue;
        if (line[i] == 'v') {
            ++i;
            if (i < line.size() && (line[i] == 'n' || line[i] == 't')) continue; // skip vn/vt
            if (i < line.size() && !(line[i] == ' ' || line[i] == '\t')) continue; // require whitespace after 'v'
            double x = 0, y = 0, z = 0; std::istringstream iss(line.substr(i));
            if (iss >> x >> y >> z) outPts.push_back(Point3{ x,y,z });
        }
        else if (line[i] == 'f') {
            ++i; if (i >= line.size()) continue;
            // tokenise remaining line by whitespace
            std::vector<int> poly;
            std::istringstream iss(line.substr(i));
            std::string tok;
            while (iss >> tok) {
                auto id = parse_index(tok, outPts.size());
                if (id) poly.push_back(*id);
            }
            if (poly.size() < 3) continue;
            for (size_t k = 2; k < poly.size(); ++k) {
                outTris.push_back(Triangle{ poly[0], poly[k - 1], poly[k] });
            }
        }
    }
    if (outPts.empty())
    {
        error = L"No vertex positions (v) found in file.";
        return false;
    }
    // Faces are optional; it's OK if outTris is empty (point cloud)
    return true;
}

static inline double Distance(const Point3& a, const Point3& b)
{
    const double dx = a.x - b.x, dy = a.y - b.y, dz = a.z - b.z;
    return std::sqrt(dx * dx + dy * dy + dz * dz);
}

static void BuildAdjacencyAndEdgeStats()
{
    g_vertexToTris.assign(g_points.size(), {});
    g_maxEdgeLen = 0.0;
    for (size_t ti = 0; ti < g_tris.size(); ++ti) {
        const auto& t = g_tris[ti];
        if (t.i0 < 0 || t.i1 < 0 || t.i2 < 0 ||
            t.i0 >= (int)g_points.size() || t.i1 >= (int)g_points.size() || t.i2 >= (int)g_points.size()) {
            continue; // ignore invalid triangles defensively
        }
        g_vertexToTris[t.i0].push_back((int)ti);
        g_vertexToTris[t.i1].push_back((int)ti);
        g_vertexToTris[t.i2].push_back((int)ti);
        const auto& a = g_points[t.i0]; const auto& b = g_points[t.i1]; const auto& c = g_points[t.i2];
        g_maxEdgeLen = std::max({ g_maxEdgeLen, Distance(a,b), Distance(b,c), Distance(c,a) });
    }
}

static double DistancePointTriangle(const Point3& p, const Point3& a, const Point3& b, const Point3& c)
{
    // Real-Time Collision Detection, Christer Ericson, closest point on triangle
    auto dot = [](double ax, double ay, double az, double bx, double by, double bz) { return ax * bx + ay * by + az * bz; };
    auto sub = [](const Point3& u, const Point3& v) { return Point3{ u.x - v.x,u.y - v.y,u.z - v.z }; };
    const Point3 ab = sub(b, a);
    const Point3 ac = sub(c, a);
    const Point3 ap = sub(p, a);
    double d1 = dot(ab.x, ab.y, ab.z, ap.x, ap.y, ap.z);
    double d2 = dot(ac.x, ac.y, ac.z, ap.x, ap.y, ap.z);
    if (d1 <= 0.0 && d2 <= 0.0) return Distance(p, a); // barycentric (1,0,0)

    const Point3 bp = sub(p, b);
    double d3 = dot(ab.x, ab.y, ab.z, bp.x, bp.y, bp.z);
    double d4 = dot(ac.x, ac.y, ac.z, bp.x, bp.y, bp.z);
    if (d3 >= 0.0 && d4 <= d3) return Distance(p, b); // barycentric (0,1,0)

    double vc = d1 * d4 - d3 * d2;
    if (vc <= 0.0 && d1 >= 0.0 && d3 <= 0.0) {
        double v = d1 / (d1 - d3);
        Point3 proj{ a.x + v * ab.x, a.y + v * ab.y, a.z + v * ab.z };
        return Distance(p, proj);
    }

    const Point3 cp = sub(p, c);
    double d5 = dot(ab.x, ab.y, ab.z, cp.x, cp.y, cp.z);
    double d6 = dot(ac.x, ac.y, ac.z, cp.x, cp.y, cp.z);
    if (d6 >= 0.0 && d5 <= d6) return Distance(p, c); // barycentric (0,0,1)

    double vb = d5 * d2 - d1 * d6;
    if (vb <= 0.0 && d2 >= 0.0 && d6 <= 0.0) {
        double w = d2 / (d2 - d6);
        Point3 proj{ a.x + w * ac.x, a.y + w * ac.y, a.z + w * ac.z };
        return Distance(p, proj);
    }

    double va = d3 * d6 - d5 * d4;
    if (va <= 0.0 && (d4 - d3) >= 0.0 && (d5 - d6) >= 0.0) {
        Point3 bc{ b.x + ((d4 - d3) / ((d4 - d3) + (d5 - d6))) * (c.x - b.x),
                   b.y + ((d4 - d3) / ((d4 - d3) + (d5 - d6))) * (c.y - b.y),
                   b.z + ((d4 - d3) / ((d4 - d3) + (d5 - d6))) * (c.z - b.z) };
        return Distance(p, bc);
    }

    // Inside face region: project to plane using barycentric coordinates
    double denom = (ab.x * ab.x + ab.y * ab.y + ab.z * ab.z) * (ac.x * ac.x + ac.y * ac.y + ac.z * ac.z) - (ab.x * ac.x + ab.y * ac.y + ab.z * ac.z) * (ab.x * ac.x + ab.y * ac.y + ab.z * ac.z);
    double s = ((ab.x * ap.x + ab.y * ap.y + ab.z * ap.z) * (ac.x * ac.x + ac.y * ac.y + ac.z * ac.z) - (ac.x * ap.x + ac.y * ap.y + ac.z * ap.z) * (ab.x * ac.x + ab.y * ac.y + ab.z * ac.z)) / denom;
    double t = ((ac.x * ap.x + ac.y * ap.y + ac.z * ap.z) * (ab.x * ab.x + ab.y * ab.y + ab.z * ab.z) - (ab.x * ap.x + ab.y * ap.y + ab.z * ap.z) * (ab.x * ac.x + ab.y * ac.y + ab.z * ac.z)) / denom;
    Point3 proj{ a.x + s * ab.x + t * ac.x, a.y + s * ab.y + t * ac.y, a.z + s * ab.z + t * ac.z };
    return Distance(p, proj);
}

static bool SphereIntersectsMeshKDRefined(double cx, double cy, double cz, double r)
{
    if (r < 0.0) r = 0.0;
    if (g_kdtree.empty()) return false;
    // Transform world-space center into model space
    Point3 cw{ cx,cy,cz };
    Point3 cm = XfApplyPointRowMajor(g_modelXf.inv, cw);
    // Conservative local radius under world->model linear map
    const double smax = XfMaxColumnNorm3x3(g_modelXf.inv);
    const double r_local = r * smax;
    const gml::KDTree3d::Point q{ cm.x, cm.y, cm.z };
    if (g_tris.empty()) {
        // Fallback: point-cloud only
        auto nearest = g_kdtree.nearest(q);
        return nearest && nearest->distance <= r_local;
    }
    const double searchR = r_local + g_maxEdgeLen; // conservative expansion to not miss edge-only intersections
    auto items = g_kdtree.radiusSearch(q, searchR);
    if (items.empty()) return false; // no nearby vertices at all
    std::vector<char> marked(g_tris.size(), 0);
    std::vector<int> candidates;
    candidates.reserve(items.size() * 6);
    for (const auto& it : items) {
        int vi = (int)std::llround(it.value);
        if (vi < 0 || vi >= (int)g_vertexToTris.size()) continue;
        for (int ti : g_vertexToTris[vi]) {
            if (!marked[ti]) { marked[ti] = 1; candidates.push_back(ti); }
        }
    }
    const Point3 center{ cx, cy, cz };
    for (int ti : candidates) {
        const auto& t = g_tris[ti];
        const auto& a = g_points[t.i0];
        const auto& b = g_points[t.i1];
        const auto& c = g_points[t.i2];
        const double d = DistancePointTriangle(center, a, b, c);
        if (d <= r) return true;
    }
    return false;
}

// ---------- Runtime single-input dialog implementation ----------
namespace {
#pragma pack(push, 1)
    struct DLGTEMPLATE_WRITER {
        HGLOBAL hgl = nullptr;
        BYTE* base = nullptr;
        BYTE* p = nullptr;
        bool init(size_t bytes) {
            hgl = GlobalAlloc(GMEM_ZEROINIT, bytes);
            if (!hgl) return false;
            base = (BYTE*)GlobalLock(hgl);
            p = base;
            return base != nullptr;
        }
        void fini() { if (base) GlobalUnlock(hgl); }
        void align_dword() { ULONG_PTR n = (ULONG_PTR)p; n = (n + 3) & ~3; p = (BYTE*)n; }
        template <class T> T* write(const T& v) { T* q = (T*)p; *q = v; p += sizeof(T); return q; }
        void write_word(WORD v) { write(v); }
        void write_dword(DWORD v) { write(v); }
        void write_wstr(const wchar_t* s) { while (*s) { write_word((WORD)*s++); } write_word(0); }
    };
#pragma pack(pop)
}

#define IDC_IN_TEXT  5002
#define IDC_IN_EDIT  5001

// IDs for Transform dialog controls
#define IDC_TX       5101
#define IDC_TY       5102
#define IDC_TZ       5103
#define IDC_YAWZ     5104
#define IDC_PITCHY   5105
#define IDC_ROLLX    5106
#define IDC_SCALE    5107
// Sphere dialog control IDs
#define IDC_SPHX     5201
#define IDC_SPHY     5202
#define IDC_SPHZ     5203
#define IDC_SPHR     5204

struct InputState { const wchar_t* title; const wchar_t* prompt; wchar_t* buf; int buflen; };

static INT_PTR CALLBACK InputDlgProc(HWND hDlg, UINT msg, WPARAM wParam, LPARAM lParam)
{
    if (msg == WM_INITDIALOG) {
        auto* st = reinterpret_cast<InputState*>(lParam);
        SetWindowLongPtrW(hDlg, GWLP_USERDATA, (LONG_PTR)st);
        SetWindowTextW(hDlg, st->title ? st->title : L"Input");
        SetDlgItemTextW(hDlg, IDC_IN_TEXT, st->prompt ? st->prompt : L"Enter value:");
        SetDlgItemTextW(hDlg, IDC_IN_EDIT, st->buf ? st->buf : L"0");
        HWND hEdit = GetDlgItem(hDlg, IDC_IN_EDIT);
        SetFocus(hEdit);
        return FALSE; // we set focus
    }
    else if (msg == WM_COMMAND) {
        switch (LOWORD(wParam)) {
        case IDOK: {
            auto* st = reinterpret_cast<InputState*>(GetWindowLongPtrW(hDlg, GWLP_USERDATA));
            if (st && st->buf && st->buflen > 0) {
                GetDlgItemTextW(hDlg, IDC_IN_EDIT, st->buf, st->buflen - 1);
            }
            EndDialog(hDlg, IDOK);
            return TRUE;
        }
        case IDCANCEL:
            EndDialog(hDlg, IDCANCEL);
            return TRUE;
        }
    }
    return FALSE;
}

static HGLOBAL BuildSingleInputDialogTemplate()
{
    // Create a simple dialog: [Text] [Edit] [OK] [Cancel]
    DLGTEMPLATE_WRITER w;
    if (!w.init(1024)) return nullptr;

    auto* dt = w.write<DLGTEMPLATE>({});
    dt->style = DS_MODALFRAME | DS_SETFONT | WS_POPUP | WS_CAPTION | WS_SYSMENU;
    dt->cdit = 4; // 4 controls
    dt->x = 10; dt->y = 10; dt->cx = 200; dt->cy = 60; // dialog units
    w.write_word(0); // no menu
    w.write_word(0); // default class
    w.write_wstr(L"Input"); // title placeholder
    w.write_word(8); // font size
    w.write_wstr(L"MS Shell Dlg"); // font face

    // Static text
    w.align_dword();
    auto* it = w.write<DLGITEMTEMPLATE>({});
    it->style = WS_CHILD | WS_VISIBLE | SS_LEFT;
    it->x = 7; it->y = 7; it->cx = 186; it->cy = 10; it->id = IDC_IN_TEXT;
    w.write_word(0xFFFF); w.write_word(0x0082); // static class
    w.write_wstr(L"Enter value:");
    w.write_word(0); // no creation data

    // Edit control
    w.align_dword();
    it = w.write<DLGITEMTEMPLATE>({});
    it->style = WS_CHILD | WS_VISIBLE | WS_BORDER | ES_LEFT | ES_AUTOHSCROLL;
    it->x = 7; it->y = 20; it->cx = 186; it->cy = 12; it->id = IDC_IN_EDIT;
    w.write_word(0xFFFF); w.write_word(0x0081); // edit class
    w.write_word(0); // empty title
    w.write_word(0); // no creation data

    // OK button
    w.align_dword();
    it = w.write<DLGITEMTEMPLATE>({});
    it->style = WS_CHILD | WS_VISIBLE | BS_DEFPUSHBUTTON;
    it->x = 60; it->y = 40; it->cx = 40; it->cy = 14; it->id = IDOK;
    w.write_word(0xFFFF); w.write_word(0x0080); // button class
    w.write_wstr(L"OK");
    w.write_word(0);

    // Cancel button
    w.align_dword();
    it = w.write<DLGITEMTEMPLATE>({});
    it->style = WS_CHILD | WS_VISIBLE | BS_PUSHBUTTON;
    it->x = 110; it->y = 40; it->cx = 40; it->cy = 14; it->id = IDCANCEL;
    w.write_word(0xFFFF); w.write_word(0x0080);
    w.write_wstr(L"Cancel");
    w.write_word(0);

    w.fini();
    return w.hgl;
}

// ---------- Transform dialog (single window with 7 edits) ----------

static HGLOBAL BuildTransformDialogTemplate()
{
    // Layout: labels+edits in two columns, then OK/Cancel
    DLGTEMPLATE_WRITER w;
    if (!w.init(4096)) return nullptr;

    auto* dt = w.write<DLGTEMPLATE>({});
    dt->style = DS_MODALFRAME | DS_SETFONT | WS_POPUP | WS_CAPTION | WS_SYSMENU;
    dt->cdit = 16; // 7 labels + 7 edits + 2 buttons
    dt->x = 10; dt->y = 10; dt->cx = 240; dt->cy = 130; // increased size so all controls fit
    w.write_word(0); // no menu
    w.write_word(0); // default class
    w.write_wstr(L"Set Transform (T,R,S)");
    w.write_word(8); // font size
    w.write_wstr(L"MS Shell Dlg");

    auto add_label = [&](int x,int y,const wchar_t* text){
        w.align_dword();
        auto* it = w.write<DLGITEMTEMPLATE>({});
        it->style = WS_CHILD | WS_VISIBLE | SS_LEFT;
        it->x = (short)x; it->y = (short)y; it->cx = 70; it->cy = 10; it->id = (WORD)-1; // no id
        w.write_word(0xFFFF); w.write_word(0x0082); // static class
        w.write_wstr(text); w.write_word(0);
    };
    auto add_edit = [&](int x,int y,WORD id){
        w.align_dword();
        auto* it = w.write<DLGITEMTEMPLATE>({});
        it->style = WS_CHILD | WS_VISIBLE | WS_BORDER | ES_LEFT | ES_AUTOHSCROLL;
        it->x = (short)x; it->y = (short)y; it->cx = 60; it->cy = 12; it->id = id;
        w.write_word(0xFFFF); w.write_word(0x0081); // edit class
        w.write_word(0); // empty title
        w.write_word(0);
    };

    int lx = 6, ex = 100; int y = 6; int dy = 14;
    add_label(lx, y, L"Translate X:"); add_edit(ex, y, IDC_TX); y += dy;
    add_label(lx, y, L"Translate Y:"); add_edit(ex, y, IDC_TY); y += dy;
    add_label(lx, y, L"Translate Z:"); add_edit(ex, y, IDC_TZ); y += dy;
    add_label(lx, y, L"Yaw Z (deg):"); add_edit(ex, y, IDC_YAWZ); y += dy;
    add_label(lx, y, L"Pitch Y (deg):"); add_edit(ex, y, IDC_PITCHY); y += dy;
    add_label(lx, y, L"Roll X (deg):"); add_edit(ex, y, IDC_ROLLX); y += dy;
    add_label(lx, y, L"Uniform Scale:"); add_edit(ex, y, IDC_SCALE); y += dy;

    // OK button
    w.align_dword();
    auto* it = w.write<DLGITEMTEMPLATE>({});
    it->style = WS_CHILD | WS_VISIBLE | BS_DEFPUSHBUTTON;
    it->x = 80; it->y = (short)(y + 6); it->cx = 50; it->cy = 14; it->id = IDOK;
    w.write_word(0xFFFF); w.write_word(0x0080); // button class
    w.write_wstr(L"OK"); w.write_word(0);

    // Cancel button
    w.align_dword();
    it = w.write<DLGITEMTEMPLATE>({});
    it->style = WS_CHILD | WS_VISIBLE | BS_PUSHBUTTON;
    it->x = 140; it->y = (short)(y + 6); it->cx = 60; it->cy = 14; it->id = IDCANCEL;
    w.write_word(0xFFFF); w.write_word(0x0080);
    w.write_wstr(L"Cancel"); w.write_word(0);

    w.fini();
    return w.hgl;
}

static bool ShowTransformDialog(HWND owner, TransformParams& out)
{
    HGLOBAL hgl = BuildTransformDialogTemplate();
    if (!hgl) return false;
    // Initialize with current values (default 0,0,0 angles and 1 scale)
    auto setText = [&](int id, const wchar_t* s){ SetDlgItemTextW((HWND)owner, id, s); };
    // We cannot preset easily with runtime template without WM_INIT; so do it in a minimal proc
    struct Ctx { TransformParams* p; } ctx{ &out };
    auto DlgProc = +[](HWND hDlg, UINT msg, WPARAM wParam, LPARAM lParam)->INT_PTR {
        static TransformParams* p = nullptr;
        if (msg == WM_INITDIALOG) {
            p = reinterpret_cast<Ctx*>(lParam)->p;
            // Defaults
            wchar_t buf[64];
            swprintf_s(buf, L"%.3f", p->tx); SetDlgItemTextW(hDlg, IDC_TX, buf);
            swprintf_s(buf, L"%.3f", p->ty); SetDlgItemTextW(hDlg, IDC_TY, buf);
            swprintf_s(buf, L"%.3f", p->tz); SetDlgItemTextW(hDlg, IDC_TZ, buf);
            swprintf_s(buf, L"%.3f", p->yawZ); SetDlgItemTextW(hDlg, IDC_YAWZ, buf);
            swprintf_s(buf, L"%.3f", p->pitchY); SetDlgItemTextW(hDlg, IDC_PITCHY, buf);
            swprintf_s(buf, L"%.3f", p->rollX); SetDlgItemTextW(hDlg, IDC_ROLLX, buf);
            swprintf_s(buf, L"%.3f", p->s); SetDlgItemTextW(hDlg, IDC_SCALE, buf);
            return TRUE;
        }
        if (msg == WM_COMMAND) {
            switch (LOWORD(wParam)) {
            case IDOK: {
                auto parse = [&](int id, double& v)->bool {
                    wchar_t b[128]{}; GetDlgItemTextW(hDlg, id, b, 127);
                    wchar_t* e=nullptr; double t = wcstod(b, &e);
                    if (!e || *e!=L'\0') return false; v = t; return true; };
                if (!parse(IDC_TX,p->tx) || !parse(IDC_TY,p->ty) || !parse(IDC_TZ,p->tz) ||
                    !parse(IDC_YAWZ,p->yawZ) || !parse(IDC_PITCHY,p->pitchY) || !parse(IDC_ROLLX,p->rollX) ||
                    !parse(IDC_SCALE,p->s)) {
                    MessageBoxW(hDlg, L"Please enter valid numeric values.", L"Invalid input", MB_OK | MB_ICONWARNING);
                    return TRUE; // stay open
                }
                EndDialog(hDlg, IDOK);
                return TRUE;
            }
            case IDCANCEL:
                EndDialog(hDlg, IDCANCEL); return TRUE;
            }
        }
        return FALSE;
    };
    // Seed defaults from current transform: we don't try to decompose rotation; zeros are fine
    out = TransformParams{0,0,0,0,0,0,1};
    INT_PTR r = DialogBoxIndirectParamW(hInst, (LPCDLGTEMPLATEW)GlobalLock(hgl), owner, DlgProc, (LPARAM)&ctx);
    GlobalUnlock(hgl); GlobalFree(hgl);
    return r == IDOK;
}

// ---------- Sphere–Mesh single-input dialog ----------
static HGLOBAL BuildSphereDialogTemplate()
{
    DLGTEMPLATE_WRITER w;
    if (!w.init(2048)) return nullptr;

    auto* dt = w.write<DLGTEMPLATE>({});
    dt->style = DS_MODALFRAME | DS_SETFONT | WS_POPUP | WS_CAPTION | WS_SYSMENU;
    dt->cdit = 10; // 4 labels + 4 edits + 2 buttons
    dt->x = 10; dt->y = 10; dt->cx = 220; dt->cy = 90;
    w.write_word(0); // no menu
    w.write_word(0); // default class
    w.write_wstr(L"Sphere - Mesh Inputs");
    w.write_word(8); w.write_wstr(L"MS Shell Dlg");

    auto add_label = [&](int x,int y,const wchar_t* text){
        w.align_dword(); auto* it = w.write<DLGITEMTEMPLATE>({});
        it->style = WS_CHILD | WS_VISIBLE | SS_LEFT; it->x=(short)x; it->y=(short)y; it->cx=80; it->cy=10; it->id=(WORD)-1;
        w.write_word(0xFFFF); w.write_word(0x0082); w.write_wstr(text); w.write_word(0);
    };
    auto add_edit = [&](int x,int y,WORD id){
        w.align_dword(); auto* it = w.write<DLGITEMTEMPLATE>({});
        it->style = WS_CHILD | WS_VISIBLE | WS_BORDER | ES_LEFT | ES_AUTOHSCROLL; it->x=(short)x; it->y=(short)y; it->cx=70; it->cy=12; it->id=id;
        w.write_word(0xFFFF); w.write_word(0x0081); w.write_word(0); w.write_word(0);
    };

    int lx=6, ex=95, y=6, dy=14;
    add_label(lx,y,L"Center X:"); add_edit(ex,y,IDC_SPHX); y+=dy;
    add_label(lx,y,L"Center Y:"); add_edit(ex,y,IDC_SPHY); y+=dy;
    add_label(lx,y,L"Center Z:"); add_edit(ex,y,IDC_SPHZ); y+=dy;
    add_label(lx,y,L"Radius:");   add_edit(ex,y,IDC_SPHR); y+=dy;

    // OK button
    w.align_dword(); auto* it = w.write<DLGITEMTEMPLATE>({});
    it->style = WS_CHILD | WS_VISIBLE | BS_DEFPUSHBUTTON; it->x=60; it->y=(short)(y+4); it->cx=40; it->cy=14; it->id=IDOK;
    w.write_word(0xFFFF); w.write_word(0x0080); w.write_wstr(L"OK"); w.write_word(0);

    // Cancel button
    w.align_dword(); it = w.write<DLGITEMTEMPLATE>({});
    it->style = WS_CHILD | WS_VISIBLE | BS_PUSHBUTTON; it->x=110; it->y=(short)(y+4); it->cx=50; it->cy=14; it->id=IDCANCEL;
    w.write_word(0xFFFF); w.write_word(0x0080); w.write_wstr(L"Cancel"); w.write_word(0);

    w.fini(); return w.hgl;
}

static bool ShowSphereDialog(HWND owner, SphereParams& out)
{
    HGLOBAL hgl = BuildSphereDialogTemplate(); if (!hgl) return false;
    struct Ctx { SphereParams* p; } ctx{ &out };
    auto DlgProc = +[](HWND hDlg, UINT msg, WPARAM wParam, LPARAM lParam)->INT_PTR {
        static SphereParams* p=nullptr;
        if (msg==WM_INITDIALOG){ p=reinterpret_cast<Ctx*>(lParam)->p; wchar_t b[64];
            swprintf_s(b,L"%.3f",p->cx); SetDlgItemTextW(hDlg, IDC_SPHX, b);
            swprintf_s(b,L"%.3f",p->cy); SetDlgItemTextW(hDlg, IDC_SPHY, b);
            swprintf_s(b,L"%.3f",p->cz); SetDlgItemTextW(hDlg, IDC_SPHZ, b);
            swprintf_s(b,L"%.3f",p->r);  SetDlgItemTextW(hDlg, IDC_SPHR, b);
            return TRUE; }
        if (msg==WM_COMMAND){ switch(LOWORD(wParam)){
            case IDOK:{ auto parse=[&](int id,double&v){ wchar_t b[128]{}; GetDlgItemTextW(hDlg,id,b,127); wchar_t*e=nullptr; double t=wcstod(b,&e); if(!e||*e!=L'\0') return false; v=t; return true; };
                if(!parse(IDC_SPHX,p->cx)||!parse(IDC_SPHY,p->cy)||!parse(IDC_SPHZ,p->cz)||!parse(IDC_SPHR,p->r)){
                    MessageBoxW(hDlg,L"Please enter valid numeric values.",L"Invalid input",MB_OK|MB_ICONWARNING); return TRUE; }
                EndDialog(hDlg,IDOK); return TRUE; }
            case IDCANCEL: EndDialog(hDlg,IDCANCEL); return TRUE; }
        }
        return FALSE; };
    out = SphereParams{0,0,0,1};
    INT_PTR r = DialogBoxIndirectParamW(hInst,(LPCDLGTEMPLATEW)GlobalLock(hgl),owner,DlgProc,(LPARAM)&ctx);
    GlobalUnlock(hgl); GlobalFree(hgl);
    return r==IDOK;
}

static bool PromptDouble(HWND owner, const wchar_t* title, const wchar_t* prompt, double& out)
{
    for (;;) {
        HGLOBAL hgl = BuildSingleInputDialogTemplate();
        if (!hgl) return false;
        wchar_t buf[128] = L"0";
        InputState st{ title, prompt, buf, (int)_countof(buf) };
        INT_PTR r = DialogBoxIndirectParamW(hInst, (LPCDLGTEMPLATEW)GlobalLock(hgl), owner, InputDlgProc, (LPARAM)&st);
        GlobalUnlock(hgl);
        GlobalFree(hgl);
        if (r != IDOK) return false;
        wchar_t* end = nullptr;
        double v = wcstod(buf, &end);
        if (end && *end == L'\0') { out = v; return true; }
        MessageBoxW(owner, L"Please enter a valid number.", L"Invalid input", MB_OK | MB_ICONWARNING);
    }
}
