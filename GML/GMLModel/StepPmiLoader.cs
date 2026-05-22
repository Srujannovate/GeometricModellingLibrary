using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using GMLModel.Pmi;
using HT = HelixToolkit;

namespace GMLModel
{
    /// <summary>
    /// Loads a STEP file and, in addition to the geometry already returned by
    /// <see cref="STEPModelLoader"/>, attempts to extract PMI (dimensions,
    /// tolerances, datums, notes, saved views) via OCCT's XDE/CAF pipeline.
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

        public void GenerateTessellations(Occt.TopoDS_Shape shape, out List<int> allIndices, out List<Vector3> allPositions)
        {
            try
            {
                _ = new Occt.BRepMesh_IncrementalMesh(shape, 0.1, false, 0.5, true);
            }
            catch (Exception ex)
            {

                throw ex;
            }
            
            allPositions = new List<Vector3>();
            allIndices = new List<int>();
            ExtractTriangles(shape, allPositions, allIndices);
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
        // PMI extraction — direct typed calls matching Occt.NET 7.9.0
        // =========================================================================
        private void ExtractPmiReflective(string path, PmiModel pmi)
        {
            // 1. Application singleton — exposed as a static PROPERTY in Occt.NET 7.9.0
            var app = Occt.XCAFApp_Application.Application;
            if (app == null || app.IsNULL)
            {
                Debug.WriteLine("[PMI] XCAFApp_Application.Application is null");
                return;
            }

            // 2. Create XDE document
            Occt.TDocStd_Document? doc = null;
            app.NewDocument("MDTV-XCAF", out doc);
            if (doc == null || doc.IsNULL)
            {
                Debug.WriteLine("[PMI] NewDocument produced no document");
                return;
            }

            // 3. STEP-CAF reader with all PMI modes enabled (properties, not methods)
            var cafReader = new Occt.STEPCAFControl_Reader();
            cafReader.ColorMode = true;
            cafReader.NameMode  = true;
            cafReader.LayerMode = true;
            cafReader.PropsMode = true;
            cafReader.GDTMode   = true;
            cafReader.ViewMode  = true;
            cafReader.MatMode   = true;

            // 4. Read
            var readStatus = cafReader.ReadFile(path);
            if (readStatus != Occt.IFSelect_ReturnStatus.IFSelect_RetDone)
            {
                Debug.WriteLine($"[PMI] CAF ReadFile status: {readStatus}");
                return;
            }

            // 5. Transfer into document
            if (!cafReader.Transfer(doc))
            {
                Debug.WriteLine("[PMI] CAF Transfer returned false");
                return;
            }

            // 6. Document structure
            var main = doc.Main;

            // 7a. Walk DGTs subtree for datums and dimensions
            try
            {
                var dgtsLabel = Occt.XCAFDoc_DocumentTool.DGTsLabel(main);
                if (dgtsLabel != null && !dgtsLabel.IsNull && dgtsLabel.HasChild)
                    WalkDgtsTree(dgtsLabel, pmi);
            }
            catch (Exception ex) { Debug.WriteLine($"[PMI] DGTs walk failed: {ex.Message}"); }

            // 7b. Walk Notes subtree
            try
            {
                var notesLabel = Occt.XCAFDoc_DocumentTool.NotesLabel(main);
                if (notesLabel != null && !notesLabel.IsNull && notesLabel.HasChild)
                    WalkNotesTree(notesLabel, pmi);
            }
            catch (Exception ex) { Debug.WriteLine($"[PMI] Notes walk failed: {ex.Message}"); }

            // 7c. Saved views via ViewTool (has GetViewLabels in this wrapper)
            try
            {
                var viewTool = Occt.XCAFDoc_DocumentTool.ViewTool(main);
                if (viewTool != null && !viewTool.IsNULL)
                    ExtractSavedViews(viewTool, pmi);
            }
            catch (Exception ex) { Debug.WriteLine($"[PMI] Views failed: {ex.Message}"); }
        }

        // ---- DGTs label tree (datums, dimensions) --------------------------------
        private static void WalkDgtsTree(Occt.TDF_Label root, PmiModel pmi)
        {
            var iter = new Occt.TDF_ChildIterator(root, true);
            int dimIdx = 0, datIdx = 0;

            while (iter.More)
            {
                var label = iter.Value;
                try
                {
                    // Datum
                    Occt.TDF_Attribute? datumAttr = null;
                    if (label.FindAttribute(Occt.XCAFDoc_Datum.ID, out datumAttr!) && datumAttr != null)
                    {
                        datIdx++;
                        try
                        {
                            var datum = Occt.XCAFDoc_Datum.DownCast(datumAttr.NativeInstancePtr);
                            string letter = "";
                            if (datum != null && !datum.IsNULL)
                            {
                                var nameH = datum.Name;
                                letter = nameH?.ToString() ?? "";
                                if (string.IsNullOrEmpty(letter))
                                {
                                    var obj = datum.GetObject;
                                    if (obj != null && !obj.IsNULL)
                                    {
                                        var semName = obj.SemanticName;
                                        letter = semName?.ToString() ?? "";
                                        if (string.IsNullOrEmpty(letter))
                                        {
                                            var objName = obj.Name;
                                            letter = objName?.ToString() ?? "";
                                        }
                                    }
                                }
                            }
                            if (string.IsNullOrEmpty(letter)) letter = $"D{datIdx}";
                            pmi.Annotations.Add(new Datum
                            {
                                Id = $"DAT_{datIdx}",
                                Kind = PmiKind.Datum,
                                Letter = letter,
                                DisplayText = letter,
                            });
                        }
                        catch (Exception ex) { Debug.WriteLine($"[PMI] datum {datIdx} extract failed: {ex.Message}"); }
                    }

                    // Dimension
                    Occt.TDF_Attribute? dimAttr = null;
                    if (label.FindAttribute(Occt.XCAFDoc_Dimension.ID, out dimAttr!) && dimAttr != null)
                    {
                        dimIdx++;
                        try
                        {
                            var dim = Occt.XCAFDoc_Dimension.DownCast(dimAttr.NativeInstancePtr);
                            string txt = $"Dimension {dimIdx}";
                            // DimensionObject is an incomplete wrapper — try to read what's available
                            if (dim != null && !dim.IsNULL)
                            {
                                var obj = dim.Object;
                                // Object type is mostly empty in Occt.NET 7.9.0;
                                // fall back to label tag for identification
                                txt = $"Dimension {dimIdx} (tag {label.Tag})";
                            }
                            pmi.Annotations.Add(new LinearDimension
                            {
                                Id = $"DIM_{dimIdx}",
                                Kind = PmiKind.LinearDimension,
                                DisplayText = txt,
                            });
                        }
                        catch (Exception ex) { Debug.WriteLine($"[PMI] dim {dimIdx} extract failed: {ex.Message}"); }
                    }
                }
                catch (Exception ex) { Debug.WriteLine($"[PMI] DGTs label failed: {ex.Message}"); }

                iter.Next();
            }
        }

        // ---- Notes label tree ----------------------------------------------------
        private static void WalkNotesTree(Occt.TDF_Label root, PmiModel pmi)
        {
            var iter = new Occt.TDF_ChildIterator(root, true);
            int noteIdx = 0;

            while (iter.More)
            {
                var label = iter.Value;
                try
                {
                    // Notes subtree children are note labels; try to read any text
                    if (label.NbAttributes > 0)
                    {
                        noteIdx++;
                        string text = $"Note {noteIdx} (tag {label.Tag})";

                        // Attempt to find a NoteComment attribute via its GUID
                        try
                        {
                            Occt.TDF_Attribute? noteAttr = null;
                            // XCAFDoc_NoteComment inherits from XCAFDoc_Note; try to read
                            // We don't have a usable ID property, so check for the note-comment type
                            var commentType = typeof(Occt.STEPControl_Reader).Assembly
                                .GetType("Occt.XCAFDoc_NoteComment");
                            if (commentType != null)
                            {
                                var idProp = commentType.GetProperty("ID",
                                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                                if (idProp != null)
                                {
                                    var guid = idProp.GetValue(null) as Occt.Standard_GUID;
                                    if (guid != null && label.FindAttribute(guid, out noteAttr!) && noteAttr != null)
                                    {
                                        var comment = Occt.XCAFDoc_NoteComment.DownCast(noteAttr.NativeInstancePtr);
                                        if (comment != null && !comment.IsNULL)
                                            text = comment.ToString() ?? text;
                                    }
                                }
                            }
                        }
                        catch { /* note detail extraction best-effort */ }

                        bool weld = text.IndexOf("weld", StringComparison.OrdinalIgnoreCase) >= 0;
                        PmiAnnotation ann = weld
                            ? new WeldSymbol { Text = text, Kind = PmiKind.WeldSymbol }
                            : new Note       { Text = text, Kind = PmiKind.Note       };
                        ann.Id = (weld ? "WLD_" : "NOTE_") + noteIdx;
                        ann.DisplayText = text;
                        pmi.Annotations.Add(ann);
                    }
                }
                catch (Exception ex) { Debug.WriteLine($"[PMI] note label failed: {ex.Message}"); }

                iter.Next();
            }
        }

        // ---- Saved views via ViewTool -------------------------------------------
        private static void ExtractSavedViews(Occt.XCAFDoc_ViewTool viewTool, PmiModel pmi)
        {
            var viewLabels = new Occt.TDF_LabelSequence();
            viewTool.GetViewLabels(out viewLabels);

            int i = 0;
            foreach (Occt.TDF_Label label in viewLabels)
            {
                i++;
                try
                {
                    var sv = new PmiSavedView { Name = $"View {i} (tag {label.Tag})" };
                    pmi.SavedViews.Add(sv);
                }
                catch (Exception ex) { Debug.WriteLine($"[PMI] view {i} failed: {ex.Message}"); }
            }
        }

        // ---- Utility helpers -----------------------------------------------------
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
