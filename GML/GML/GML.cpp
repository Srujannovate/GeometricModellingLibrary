// GML.cpp : Defines the entry point for the application.
//

#include "framework.h"
#include "GML.h"
#include "GMLLibrary.h"
#include <vector>
#include <string>
#include <sstream>
#include <fstream>
#include <filesystem>
#include <iomanip>
#include <optional>
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
static std::vector<Point3> g_points;            // loaded 3D points
static std::wstring g_summary;                  // text rendered in the main window

static void UpdateSummaryText();
static bool LoadOBJPoints(const std::filesystem::path& path,
                          std::vector<Point3>& out,
                          std::wstring& error);
static std::optional<std::filesystem::path> ShowOpenObjDialog(HWND owner);

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
    if (!InitInstance (hInstance, nCmdShow))
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

    return (int) msg.wParam;
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

    wcex.style          = CS_HREDRAW | CS_VREDRAW;
    wcex.lpfnWndProc    = WndProc;
    wcex.cbClsExtra     = 0;
    wcex.cbWndExtra     = 0;
    wcex.hInstance      = hInstance;
    wcex.hIcon          = LoadIcon(hInstance, MAKEINTRESOURCE(IDI_GML));
    wcex.hCursor        = LoadCursor(nullptr, IDC_ARROW);
    wcex.hbrBackground  = (HBRUSH)(COLOR_WINDOW+1);
    wcex.lpszMenuName   = MAKEINTRESOURCEW(IDC_GML);
    wcex.lpszClassName  = szWindowClass;
    wcex.hIconSm        = LoadIcon(wcex.hInstance, MAKEINTRESOURCE(IDI_SMALL));

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
       AppendMenuW(hMenu, MF_STRING, IDM_LOADOBJ, L"&Load OBJ...");
       DrawMenuBar(hWnd);
   }

   ShowWindow(hWnd, nCmdShow);
   UpdateWindow(hWnd);

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
                    std::vector<Point3> tmp;
                    if (LoadOBJPoints(*sel, tmp, error))
                    {
                        g_points = std::move(tmp);
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
            wchar_t bufA[64] = {0}, bufB[64] = {0};
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
    oss << L"Format: index: x y z\r\n\r\n";
    oss << std::setprecision(6);
    for (size_t i = 0; i < g_points.size(); ++i)
    {
        const auto& p = g_points[i];
        oss << i << L": " << p.x << L"\t" << p.y << L"\t" << p.z << L"\r\n";
    }
    g_summary = oss.str();
}

// Show a file-open dialog filtered to .obj files.
static std::optional<std::filesystem::path> ShowOpenObjDialog(HWND owner)
{
    wchar_t fileBuf[MAX_PATH] = {0};
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
        return std::filesystem::path{fileBuf};
    }
    return std::nullopt;
}

// Minimal OBJ parser: collects lines that start with "v " as 3D points.
static bool LoadOBJPoints(const std::filesystem::path& path,
                          std::vector<Point3>& out,
                          std::wstring& error)
{
    out.clear();
    std::ifstream in(path, std::ios::in);
    if (!in.is_open())
    {
        error = L"Could not open file: " + path.wstring();
        return false;
    }
    std::string line;
    while (std::getline(in, line))
    {
        // Skip leading spaces
        size_t i = 0;
        while (i < line.size() && (line[i] == ' ' || line[i] == '\t')) ++i;
        if (i >= line.size()) continue;
        if (line[i] != 'v') continue; // only vertex positions
        ++i;
        if (i < line.size() && (line[i] == 'n' || line[i] == 't')) continue; // skip vn/vt
        if (i < line.size() && !(line[i] == ' ' || line[i] == '\t')) continue; // require whitespace after 'v'
        // Parse x y z (ignore optional w)
        double x = 0, y = 0, z = 0;
        std::istringstream iss(line.substr(i));
        if (iss >> x >> y >> z)
        {
            out.push_back(Point3{x, y, z});
        }
    }
    if (out.empty())
    {
        error = L"No vertex positions (v) found in file.";
        return false;
    }
    return true;
}
