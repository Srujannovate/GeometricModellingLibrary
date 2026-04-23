using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using GMLModel.Pmi;
using HT = HelixToolkit;

namespace GMLModel
{
    /// <summary>
    /// Loads a STEP file and, in addition to the geometry already returned by
    /// <see cref="STEPModelLoader"/>, attempts to extract PMI (dimensions,
    /// tolerances, datums, notes, saved views) via OCCT's XDE/CAF pipeline.
    ///
    /// The Occt.NET C++/CLI wrapper tends to change member names between
    /// releases (properties vs. methods, renamed setters, indexers), so all of
    /// the XCAF calls go through reflection. When a member is missing the
    /// corresponding PMI section is skipped and the rest of the loader keeps
    /// going.
    /// </summary>
    public class StepPmiLoader
    {
        public PmiModel Load(string path,
            out List<int> allIndices,
            out List<Vector3> allPositions,
            out HT.IntCollection lineIndices,
            out HT.Vector3Collection linePositions)
        {
            OcctConfiguration.Configure();

            allPositions  = new List<Vector3>();
            allIndices    = new List<int>();
            linePositions = new HT.Vector3Collection();
            lineIndices   = new HT.IntCollection();

            var pmi = new PmiModel();

            // --- Tessellation via the plain reader (identical to STEPModelLoader) -----
            var reader = new Occt.STEPControl_Reader();
            var status = reader.ReadFile(path);
            if (status != Occt.IFSelect_ReturnStatus.IFSelect_RetDone)
                throw new InvalidOperationException($"Failed to read STEP file: {Path.GetFileName(path)} (status={status})");

            reader.TransferRoots();
            var shape = reader.OneShape();
            if (shape == null || shape.IsNull)
                throw new InvalidOperationException($"No geometry found in: {Path.GetFileName(path)}");

            _ = new Occt.BRepMesh_IncrementalMesh(shape, 0.1, false, 0.5, true);
            ExtractTriangles(shape, allPositions, allIndices);
            ExtractEdges(shape, linePositions, lineIndices);

            // --- Best-effort PMI extraction via reflection ---------------------------
            try { pmi.LengthUnitMm = DetectLengthUnitMm(); } catch { }
            try { ExtractPmiReflective(path, pmi); }
            catch (Exception ex) { Debug.WriteLine($"[PMI] extraction skipped: {ex.Message}"); }

            return pmi;
        }

        // =========================================================================
        // Tessellation (copied verbatim from STEPModelLoader)
        // =========================================================================
        private static void ExtractTriangles(Occt.TopoDS_Shape shape, List<Vector3> positions, List<int> indices)
        {
            var explorer = new Occt.TopExp_Explorer(shape, Occt.TopAbs_ShapeEnum.TopAbs_FACE);
            while (explorer.More)
            {
                var face = (Occt.TopoDS_Face)explorer.Current;
                var location = new Occt.TopLoc_Location();
                var poly = Occt.BRep_Tool.Triangulation(face, out location);
                if (poly != null)
                {
                    int vertexOffset = positions.Count;
                    var trsf = location.IsIdentity ? null : location.Transformation;
                    for (int i = 1; i <= poly.NbNodes; i++)
                    {
                        var pt = poly.Node(i);
                        if (trsf != null) pt.Transform(trsf);
                        positions.Add(new Vector3((float)pt.X, (float)pt.Y, (float)pt.Z));
                    }
                    bool reversed = face.Orientation == Occt.TopAbs_Orientation.TopAbs_REVERSED;
                    for (int i = 1; i <= poly.NbTriangles; i++)
                    {
                        var triangle = poly.Triangle(i);
                        int n1 = 0, n2 = 0, n3 = 0;
                        triangle.Get(out n1, out n2, out n3);
                        if (reversed)
                        {
                            indices.Add(vertexOffset + n1 - 1);
                            indices.Add(vertexOffset + n3 - 1);
                            indices.Add(vertexOffset + n2 - 1);
                        }
                        else
                        {
                            indices.Add(vertexOffset + n1 - 1);
                            indices.Add(vertexOffset + n2 - 1);
                            indices.Add(vertexOffset + n3 - 1);
                        }
                    }
                }
                explorer.Next();
            }
        }

        private static void ExtractEdges(Occt.TopoDS_Shape shape, HT.Vector3Collection positions, HT.IntCollection indices)
        {
            var edgeExplorer = new Occt.TopExp_Explorer(shape, Occt.TopAbs_ShapeEnum.TopAbs_EDGE);
            while (edgeExplorer.More)
            {
                var edge = (Occt.TopoDS_Edge)edgeExplorer.Current;
                var edgeLoc = new Occt.TopLoc_Location();
                var polygon = Occt.BRep_Tool.Polygon3D(edge, out edgeLoc);
                if (polygon != null)
                {
                    var edgeTrsf = edgeLoc.IsIdentity ? null : edgeLoc.Transformation;
                    int nbNodes = polygon.NbNodes;
                    int baseIdx = positions.Count;
                    var nodes = polygon.Nodes;
                    for (int i = 1; i <= nbNodes; i++)
                    {
                        var pt = nodes[i];
                        if (edgeTrsf != null) pt.Transform(edgeTrsf);
                        positions.Add(new Vector3((float)pt.X, (float)pt.Y, (float)pt.Z));
                    }
                    for (int i = 0; i < nbNodes - 1; i++)
                    {
                        indices.Add(baseIdx + i);
                        indices.Add(baseIdx + i + 1);
                    }
                }
                edgeExplorer.Next();
            }
        }

        // =========================================================================
        // PMI extraction via reflection
        // =========================================================================
        private void ExtractPmiReflective(string path, PmiModel pmi)
        {
            var asm = typeof(Occt.STEPControl_Reader).Assembly;

            var cafReaderType = asm.GetType("Occt.STEPCAFControl_Reader");
            var appType       = asm.GetType("Occt.XCAFApp_Application");
            var docType       = asm.GetType("Occt.TDocStd_Document");
            var docToolType   = asm.GetType("Occt.XCAFDoc_DocumentTool");
            if (cafReaderType == null || appType == null || docType == null || docToolType == null)
            {
                Debug.WriteLine("[PMI] XCAF types unavailable — skipping PMI.");
                return;
            }

            // Obtain app singleton (Instance / GetApplication / default ctor)
            object? app = null;
            try { app = appType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null); } catch { }
            if (app == null) try { app = appType.GetMethod("GetApplication", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null); } catch { }
            if (app == null) try { app = Activator.CreateInstance(appType); } catch { }
            if (app == null) { Debug.WriteLine("[PMI] no XCAFApp_Application instance"); return; }

            // Create document
            object? doc = null;
            var newDoc = appType.GetMethods().FirstOrDefault(m => m.Name == "NewDocument" && m.GetParameters().Length >= 2);
            if (newDoc == null) { Debug.WriteLine("[PMI] NewDocument not found"); return; }
            var pars = newDoc.GetParameters();
            var args = new object?[pars.Length];
            args[0] = "MDTV-XCAF";
            try { newDoc.Invoke(app, args); doc = args[1]; } catch (Exception ex) { Debug.WriteLine($"[PMI] NewDocument failed: {ex.Message}"); }
            if (doc == null) { Debug.WriteLine("[PMI] no document produced"); return; }

            // Create CAF reader and enable PMI modes
            object? cafReader = null;
            try { cafReader = Activator.CreateInstance(cafReaderType); } catch { }
            if (cafReader == null) { Debug.WriteLine("[PMI] STEPCAFControl_Reader ctor failed"); return; }
            foreach (var setter in new[] { "SetColorMode", "SetNameMode", "SetLayerMode", "SetPropsMode", "SetGDTMode", "SetViewMode", "SetMatMode" })
                TryInvoke(cafReader, setter, true);
            foreach (var prop in new[] { "ColorMode", "NameMode", "LayerMode", "PropsMode", "GDTMode", "ViewMode", "MatMode" })
                TrySetProperty(cafReader, prop, true);

            // ReadFile
            object? readResult = null;
            try
            {
                var readMethod = cafReader.GetType().GetMethods().FirstOrDefault(m => m.Name == "ReadFile" && m.GetParameters().Length == 1);
                readResult = readMethod?.Invoke(cafReader, new object?[] { path });
            }
            catch (Exception ex) { Debug.WriteLine($"[PMI] ReadFile(caf) failed: {ex.Message}"); return; }
            if (readResult == null) return;
            try
            {
                if (Convert.ToInt32(readResult) != 0) { Debug.WriteLine($"[PMI] ReadFile(caf) status={readResult}"); return; }
            }
            catch { /* enum ToString not convertible; ignore */ }

            // Transfer
            var transfer = cafReader.GetType().GetMethods().FirstOrDefault(m =>
                m.Name == "Transfer" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.IsAssignableFrom(docType));
            if (transfer == null) { Debug.WriteLine("[PMI] Transfer(doc) not found"); return; }
            object? xferOk = null;
            try { xferOk = transfer.Invoke(cafReader, new[] { doc }); } catch (Exception ex) { Debug.WriteLine($"[PMI] Transfer failed: {ex.Message}"); return; }
            if (xferOk is bool bOk && !bOk) { Debug.WriteLine("[PMI] Transfer returned false"); return; }

            // doc.Main (property / field / method)
            var mainLabel = GetMember(doc, "Main");
            if (mainLabel == null) { Debug.WriteLine("[PMI] doc.Main not accessible"); return; }

            var dimTolTool = InvokeStatic(docToolType, "DimTolTool", mainLabel);
            var notesTool  = InvokeStatic(docToolType, "NotesTool",  mainLabel);
            var viewTool   = InvokeStatic(docToolType, "ViewTool",   mainLabel);

            if (dimTolTool != null)
            {
                ExtractDimensions(dimTolTool, pmi);
                ExtractTolerances(dimTolTool, pmi);
                ExtractDatums(dimTolTool, pmi);
            }
            if (notesTool != null) ExtractNotes(notesTool, pmi);
            if (viewTool  != null) ExtractSavedViews(viewTool, pmi);
        }

        // ---- Section extractors --------------------------------------------------
        private static void ExtractDimensions(object dimTolTool, PmiModel pmi)
        {
            var labels = GetLabels(dimTolTool, new[] { "GetDimensionLabels", "GetDimensions", "DimensionLabels" });
            int i = 0;
            foreach (var label in labels)
            {
                i++;
                try
                {
                    string txt = ReadText(label) ?? "dim";
                    double val = ReadDouble(label, new[] { "GetValue", "Value" });
                    var attach = ReadPoint(label) ?? Vector3.Zero;
                    pmi.Annotations.Add(new LinearDimension
                    {
                        Id = $"DIM_{i}",
                        Kind = PmiKind.LinearDimension,
                        DisplayText = val != 0 ? $"{val:0.###}" : txt,
                        Value = val,
                        AttachPoint = attach,
                        TextPosition = attach + new Vector3(0, 0, 5),
                    });
                }
                catch (Exception ex) { Debug.WriteLine($"[PMI] dim {i} failed: {ex.Message}"); }
            }
        }

        private static void ExtractTolerances(object dimTolTool, PmiModel pmi)
        {
            var labels = GetLabels(dimTolTool, new[] { "GetGeomToleranceLabels", "GetGeomTolerances", "GDTLabels" });
            int i = 0;
            foreach (var label in labels)
            {
                i++;
                try
                {
                    double val = ReadDouble(label, new[] { "GetValue", "Value" });
                    string raw = ReadText(label) ?? string.Empty;
                    var sym = FcfSymbol.Unknown;
                    if (raw.Contains("Flat", StringComparison.OrdinalIgnoreCase)) sym = FcfSymbol.Flatness;
                    else if (raw.Contains("Position", StringComparison.OrdinalIgnoreCase)) sym = FcfSymbol.Position;
                    else if (raw.Contains("Parallel", StringComparison.OrdinalIgnoreCase)) sym = FcfSymbol.Parallelism;
                    else if (raw.Contains("Perpend", StringComparison.OrdinalIgnoreCase)) sym = FcfSymbol.Perpendicularity;
                    var fcf = new FeatureControlFrame
                    {
                        Symbol = sym,
                        ToleranceValue = val,
                        FormattedText = $"{GlyphFor(sym)} {val:0.###}"
                    };
                    pmi.Annotations.Add(new GeometricTolerance
                    {
                        Id = $"TOL_{i}",
                        Kind = PmiKind.GeometricTolerance,
                        DisplayText = fcf.FormattedText,
                        FCF = fcf,
                    });
                }
                catch (Exception ex) { Debug.WriteLine($"[PMI] tol {i} failed: {ex.Message}"); }
            }
        }

        private static void ExtractDatums(object dimTolTool, PmiModel pmi)
        {
            var labels = GetLabels(dimTolTool, new[] { "GetDatumLabels", "GetDatums", "DatumLabels" });
            int i = 0;
            foreach (var label in labels)
            {
                i++;
                try
                {
                    string letter = ReadText(label) ?? $"D{i}";
                    pmi.Annotations.Add(new Datum
                    {
                        Id = $"DAT_{i}",
                        Kind = PmiKind.Datum,
                        Letter = letter,
                        DisplayText = letter,
                    });
                }
                catch (Exception ex) { Debug.WriteLine($"[PMI] datum {i} failed: {ex.Message}"); }
            }
        }

        private static void ExtractNotes(object notesTool, PmiModel pmi)
        {
            var labels = GetLabels(notesTool, new[] { "GetNotes", "GetNoteLabels", "NoteLabels" });
            int i = 0;
            foreach (var label in labels)
            {
                i++;
                try
                {
                    string text = ReadText(label) ?? $"Note {i}";
                    bool weld = text.IndexOf("weld", StringComparison.OrdinalIgnoreCase) >= 0;
                    PmiAnnotation ann = weld
                        ? new WeldSymbol { Text = text, Kind = PmiKind.WeldSymbol }
                        : new Note       { Text = text, Kind = PmiKind.Note       };
                    ann.Id = (weld ? "WLD_" : "NOTE_") + i;
                    ann.DisplayText = text;
                    pmi.Annotations.Add(ann);
                }
                catch (Exception ex) { Debug.WriteLine($"[PMI] note {i} failed: {ex.Message}"); }
            }
        }

        private static void ExtractSavedViews(object viewTool, PmiModel pmi)
        {
            var labels = GetLabels(viewTool, new[] { "GetViewLabels", "GetViews", "ViewLabels" });
            int i = 0;
            foreach (var label in labels)
            {
                i++;
                try
                {
                    var sv = new PmiSavedView { Name = ReadText(label) ?? $"View {i}" };
                    var eye = ReadPoint(label);
                    if (eye.HasValue) sv.Eye = eye.Value;
                    pmi.SavedViews.Add(sv);
                }
                catch (Exception ex) { Debug.WriteLine($"[PMI] view {i} failed: {ex.Message}"); }
            }
        }

        // ---- Reflection helpers -------------------------------------------------
        private static IEnumerable<object?> GetLabels(object tool, string[] memberNames)
        {
            object? sequence = null;
            foreach (var n in memberNames)
            {
                var methods = tool.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy).Where(m => m.Name == n);
                foreach (var m in methods)
                {
                    var mp = m.GetParameters();
                    try
                    {
                        if (mp.Length == 0) { sequence = m.Invoke(tool, null); }
                        else if (mp.Length == 1 && mp[0].ParameterType.Name.Contains("LabelSequence"))
                        {
                            var seq = Activator.CreateInstance(mp[0].ParameterType);
                            m.Invoke(tool, new[] { seq });
                            sequence = seq;
                        }
                    }
                    catch (Exception ex) { Debug.WriteLine($"[PMI] {n} failed: {ex.Message}"); }
                    if (sequence != null) break;
                }
                if (sequence != null) break;
                var p = tool.GetType().GetProperty(n);
                if (p != null) { try { sequence = p.GetValue(tool); } catch { } }
                if (sequence != null) break;
            }
            return EnumerateSequence(sequence);
        }

        private static IEnumerable<object?> EnumerateSequence(object? seq)
        {
            if (seq == null) yield break;

            if (seq is IEnumerable en && seq is not string)
            {
                foreach (var item in en) yield return item;
                yield break;
            }

            var lenProp = seq.GetType().GetProperty("Length")
                       ?? seq.GetType().GetProperty("Count")
                       ?? seq.GetType().GetProperty("NbLabels");
            int len = 0;
            try { if (lenProp != null) len = Convert.ToInt32(lenProp.GetValue(seq)); } catch { }

            var valueMethod = seq.GetType().GetMethod("Value", new[] { typeof(int) });
            var itemMethod  = seq.GetType().GetMethod("Item",  new[] { typeof(int) });
            var indexer = seq.GetType().GetProperties().FirstOrDefault(p =>
                p.GetIndexParameters().Length == 1 && p.GetIndexParameters()[0].ParameterType == typeof(int));

            for (int i = 1; i <= len; i++)
            {
                object? item = null;
                try
                {
                    if (valueMethod != null) item = valueMethod.Invoke(seq, new object[] { i });
                    else if (itemMethod != null) item = itemMethod.Invoke(seq, new object[] { i });
                    else if (indexer != null) item = indexer.GetValue(seq, new object[] { i });
                }
                catch { }
                yield return item;
            }
        }

        private static string? ReadText(object? label)
        {
            if (label == null) return null;
            foreach (var name in new[] { "UserName", "Name", "GetName", "SemanticName", "GetSemanticName" })
            {
                try
                {
                    var m = label.GetType().GetMethod(name, Type.EmptyTypes);
                    if (m != null) { var r = m.Invoke(label, null); if (r != null) return r.ToString(); }
                    var p = label.GetType().GetProperty(name);
                    if (p != null) { var r = p.GetValue(label); if (r != null) return r.ToString(); }
                }
                catch { }
            }
            return label.ToString();
        }

        private static double ReadDouble(object? label, string[] names)
        {
            if (label == null) return 0;
            foreach (var n in names)
            {
                try
                {
                    var m = label.GetType().GetMethod(n, Type.EmptyTypes);
                    if (m != null && (m.ReturnType == typeof(double) || m.ReturnType == typeof(float)))
                    {
                        var r = m.Invoke(label, null);
                        if (r != null) return Convert.ToDouble(r);
                    }
                    var p = label.GetType().GetProperty(n);
                    if (p != null && (p.PropertyType == typeof(double) || p.PropertyType == typeof(float)))
                    {
                        var r = p.GetValue(label);
                        if (r != null) return Convert.ToDouble(r);
                    }
                }
                catch { }
            }
            return 0;
        }

        private static Vector3? ReadPoint(object? label)
        {
            if (label == null) return null;
            foreach (var n in new[] { "GetPoint", "Point", "GetCenter", "Center", "ProjectionPoint", "Origin" })
            {
                try
                {
                    var m = label.GetType().GetMethod(n, Type.EmptyTypes);
                    object? pt = m?.Invoke(label, null) ?? label.GetType().GetProperty(n)?.GetValue(label);
                    if (pt == null) continue;
                    double x = TryD(pt, "X"), y = TryD(pt, "Y"), z = TryD(pt, "Z");
                    if (x != 0 || y != 0 || z != 0)
                        return new Vector3((float)x, (float)y, (float)z);
                }
                catch { }
            }
            return null;
        }

        private static double TryD(object o, string member)
        {
            try
            {
                var p = o.GetType().GetProperty(member);
                if (p != null) { var v = p.GetValue(o); if (v != null) return Convert.ToDouble(v); }
                var m = o.GetType().GetMethod(member, Type.EmptyTypes);
                if (m != null) { var v = m.Invoke(o, null); if (v != null) return Convert.ToDouble(v); }
            }
            catch { }
            return 0;
        }

        private static object? GetMember(object target, string name)
        {
            var p = target.GetType().GetProperty(name);
            if (p != null) try { return p.GetValue(target); } catch { }
            var f = target.GetType().GetField(name);
            if (f != null) try { return f.GetValue(target); } catch { }
            var m = target.GetType().GetMethod(name, Type.EmptyTypes);
            if (m != null) try { return m.Invoke(target, null); } catch { }
            return null;
        }

        private static object? InvokeStatic(Type type, string name, params object?[] args)
        {
            var m = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(mi => mi.Name == name && mi.GetParameters().Length == args.Length);
            try { return m?.Invoke(null, args); }
            catch (Exception ex) { Debug.WriteLine($"[PMI] {type.Name}.{name} failed: {ex.Message}"); return null; }
        }

        private static void TryInvoke(object target, string name, params object?[] args)
        {
            try
            {
                var m = target.GetType().GetMethods()
                    .FirstOrDefault(mi => mi.Name == name && mi.GetParameters().Length == args.Length);
                m?.Invoke(target, args);
            }
            catch { }
        }

        private static void TrySetProperty(object target, string name, object? value)
        {
            try { target.GetType().GetProperty(name)?.SetValue(target, value); } catch { }
        }

        private static string GlyphFor(FcfSymbol s) => s switch
        {
            FcfSymbol.Flatness         => "⏥",
            FcfSymbol.Position         => "⌖",
            FcfSymbol.Parallelism      => "∥",
            FcfSymbol.Perpendicularity => "⊥",
            FcfSymbol.Circularity      => "○",
            FcfSymbol.Concentricity    => "◎",
            _ => "?",
        };

        private static double DetectLengthUnitMm()
        {
            try
            {
                int u = Occt.Interface_Static.IVal("xstep.cascade.unit");
                return u switch
                {
                    1 => 1000.0,
                    2 => 25.4,
                    3 => 304.8,
                    6 => 0.001,
                    7 => 10.0,
                    8 => 1_000_000.0,
                    _ => 1.0,
                };
            }
            catch { return 1.0; }
        }
    }
}
