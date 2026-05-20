using System.Text.Json;
using FloorPlan.Editor.Model;
using FloorPlan.Editor.Persistence;

namespace FloorPlan.Editor;

public enum DragMode { None, PanView, Node, Wall, Handle, Measure }

public partial class EditorEngine
{
    public FloorPlanModel Plan { get; private set; } = new();
    public Viewport Vp { get; } = new();
    public Tool ActiveTool { get; private set; } = Tool.View;
    public bool Snap { get; set; } = true;
    public Furniture? Selected { get; private set; }
    public string? Notification { get; private set; }

    public WallType CurrentWallType { get; private set; } = WallType.Exterior;
    public bool WallsLocked { get; private set; }
    public bool ShowRoomArea { get; private set; }

    public event Action? Changed;

    // pointer indicator (world)
    private Pt _mouseWorld;

    // wall-add chain
    private int? _prevNodeId;
    private Pt? _wallPreviewStart;

    // measure
    private Pt? _measureStart;
    private Pt _measureCur;

    // drag
    private DragMode _drag = DragMode.None;
    private int _dragNodeId;
    private Wall? _dragWall;
    private Pt _dragStartWorld, _dragStartLeft, _dragStartRight;
    private HandleType _dragHandle;
    private double _hStartScaleX, _hStartScaleY, _hStartRot, _hStartDist;
    private double _hStartAngle;
    private Pt _hStartCenter;
    private double _hStartLocalX;

    // right-button (pan vs click-to-toggle)
    private bool _rightDown;
    private double _rightDownX, _rightDownY;
    private object? _rightTarget;

    // hover target (for Delete-key + highlight)
    private Furniture? _hoverFur;
    private WallNode? _hoverNode;
    private Wall? _hoverWall;

    private void Notify(string? msg = null)
    {
        Notification = msg;
        Changed?.Invoke();
    }

    public void SetTool(Tool t)
    {
        ActiveTool = t;
        ResetTools();
        Notify();
    }

    public void SetNotice(string msg) => Notify(msg);

    public void Deselect()
    {
        if (Selected != null) { Selected = null; Notify("Deselected"); }
    }

    // arrow keys: nudge the selected item, else pan the view
    public void ArrowKey(double dirX, double dirY)
    {
        if (Selected != null)
        {
            PushUndo();
            var f = Selected;
            double step = Snap ? 10 : 5;
            if (f.IsAttached && f.AttachedTo != null)
                f.X = Math.Clamp(f.X + dirX * step, 0, Math.Max(0, f.AttachedTo.Length - f.Width));
            else { f.X += dirX * step; f.Y += dirY * step; }
            Notify();
        }
        else
        {
            // camera moves in the arrow direction
            Vp.PanBy(-dirX * 60, -dirY * 60);
            Changed?.Invoke();
        }
    }

    public void SetSnap(bool s) { Snap = s; Notify(); }
    public void ToggleLabels() { Plan.LabelsVisible = !Plan.LabelsVisible; Notify(); }

    public void SetWallType(WallType t)
    {
        CurrentWallType = t;
        Notify($"Wall type: {WallTypeName(t)}");
    }

    public void ToggleLock()
    {
        WallsLocked = !WallsLocked;
        if (WallsLocked) { _prevNodeId = null; _wallPreviewStart = null; }
        Notify(WallsLocked ? "Walls locked" : "Walls unlocked");
    }

    public void ToggleRoomArea() { ShowRoomArea = !ShowRoomArea; Notify(); }

    public static string WallTypeName(WallType t) => t switch
    {
        WallType.Exterior => "Exterior",
        WallType.RoomDivider => "Room divider",
        _ => "Interior"
    };

    private void ResetTools()
    {
        Selected = null;
        _prevNodeId = null;
        _wallPreviewStart = null;
        _measureStart = null;
        _drag = DragMode.None;
        _hoverFur = null;
        _hoverNode = null;
        _hoverWall = null;
    }

    public void NewPlan()
    {
        WallNodeSequence.ResetIdCounter();
        Plan = new FloorPlanModel();
        _undo.Clear();
        _redo.Clear();
        ResetTools();
        MutationVersion++;
        Notify("New plan");
    }

    public void Resize(double w, double h) { Vp.Resize(w, h); Changed?.Invoke(); }

    // ---- floor controls ----
    public void ChangeFloor(int by) { Plan.ChangeFloor(by); ResetTools(); MutationVersion++; Notify(); }
    public void DeleteFloor()
    {
        int before = _undo.Count;
        PushUndo();
        bool ok = Plan.RemoveFloor();
        if (!ok && _undo.Count > before) _undo.RemoveAt(_undo.Count - 1);
        ResetTools();
        Notify(ok ? null : Plan.Notice);
    }

    // ---- persistence ----
    public string Save() => Plan.Save();
    public void Load(string text)
    {
        try
        {
            WallNodeSequence.ResetIdCounter();
            Plan.Load(text);
            _undo.Clear();
            _redo.Clear();
            ResetTools();
            MutationVersion++;
            Notify("Plan loaded");
        }
        catch (Exception e)
        {
            Notify("Could not load file: " + e.Message);
        }
    }

    // bumped on any mutation that changes plan content or undo/redo stacks.
    // EditorPage watches this to autosave the full state to the browser
    public int MutationVersion { get; private set; }

    public string SaveFullState()
    {
        var fs = new FullStateSerializable
        {
            plan = Plan.Save(),
            floor = Plan.CurrentNumber,
        };
        foreach (var (json, floor) in _undo)
            fs.undo.Add(new HistoryEntrySerializable { json = json, floor = floor });
        foreach (var (json, floor) in _redo)
            fs.redo.Add(new HistoryEntrySerializable { json = json, floor = floor });
        return JsonSerializer.Serialize(fs);
    }

    public bool TryLoadFullState(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        try
        {
            var fs = JsonSerializer.Deserialize<FullStateSerializable>(text);
            if (fs == null || string.IsNullOrEmpty(fs.plan)) return false;
            WallNodeSequence.ResetIdCounter();
            Plan.Load(fs.plan);
            Plan.SetFloor(fs.floor);
            _undo.Clear();
            foreach (var e in fs.undo) _undo.Add((e.json, e.floor));
            _redo.Clear();
            foreach (var e in fs.redo) _redo.Add((e.json, e.floor));
            ResetTools();
            MutationVersion++;
            Changed?.Invoke();
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ===== undo / redo (whole-plan snapshots) =====
    private const int UNDO_MAX = 80;
    private readonly List<(string json, int floor)> _undo = new();
    private readonly List<(string json, int floor)> _redo = new();

    // capture the state *before* a mutation; deduped so no-op clicks don't bloat history
    private void PushUndo()
    {
        var snap = (Plan.Save(), Plan.CurrentNumber);
        if (_undo.Count > 0 && _undo[^1].json == snap.Item1 && _undo[^1].floor == snap.Item2)
            return;
        _undo.Add(snap);
        if (_undo.Count > UNDO_MAX) _undo.RemoveAt(0);
        _redo.Clear();
        MutationVersion++;
    }

    private void ApplyState((string json, int floor) st)
    {
        WallNodeSequence.ResetIdCounter();
        Plan.Load(st.json);
        Plan.SetFloor(st.floor);
        ResetTools();
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Undo()
    {
        if (_undo.Count == 0) { Notify("Nothing to undo"); return; }
        _redo.Add((Plan.Save(), Plan.CurrentNumber));
        var st = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        ApplyState(st);
        MutationVersion++;
        Notify("Undo");
    }

    public void Redo()
    {
        if (_redo.Count == 0) { Notify("Nothing to redo"); return; }
        _undo.Add((Plan.Save(), Plan.CurrentNumber));
        var st = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        ApplyState(st);
        MutationVersion++;
        Notify("Redo");
    }

    // Delete key: removes the selected furniture, or whatever is under the cursor
    public void DeleteSelected()
    {
        var fur = Selected ?? _hoverFur;
        if (fur != null)
        {
            PushUndo();
            Plan.Current.RemoveFurniture(fur.Id);
            if (Selected == fur) Selected = null;
            _hoverFur = null;
            Notify("Deleted");
            return;
        }

        if (_hoverNode != null)
        {
            if (WallsLocked) { Notify("Walls are locked"); return; }
            int before = _undo.Count;
            PushUndo();
            bool ok = Plan.Current.Seq.RemoveNode(_hoverNode.Id);
            if (!ok && _undo.Count > before) _undo.RemoveAt(_undo.Count - 1);
            if (ok) _hoverNode = null;
            Notify(ok ? "Deleted" : Plan.Current.Seq.LastError);
            return;
        }

        if (_hoverWall != null)
        {
            if (WallsLocked) { Notify("Walls are locked"); return; }
            PushUndo();
            Plan.Current.Seq.RemoveWall(_hoverWall.LeftNode.Id, _hoverWall.RightNode.Id);
            Plan.Current.Seq.PruneIsolatedNodes();
            _hoverWall = null;
            Notify("Deleted");
            return;
        }

        Notify("Nothing under cursor");
    }

    // add free furniture at centre of current view
    public void AddCatalogFurniture(CatalogItem item)
    {
        PushUndo();
        Pt c = Vp.ScreenToWorld(Vp.ScreenW / 2, Vp.ScreenH / 2);
        if (Snap) { c.X = Geometry.Snap(c.X); c.Y = Geometry.Snap(c.Y); }
        Plan.AddFurniture(item, null, c);
        Notify($"Added {item.Name}");
    }

    private double NextZ()
    {
        int max = 1;
        foreach (var f in Plan.Current.FurnitureMap.Values) max = Math.Max(max, f.ZIndex);
        return max + 1;
    }

    // ===== input =====
    public void PointerDown(int button, double sx, double sy)
    {
        _mouseWorld = Vp.ScreenToWorld(sx, sy);

        if (button == 2)
        {
            _rightDown = true;
            _rightDownX = sx;
            _rightDownY = sy;
            // right-click cancels the current wall chain instead of retyping
            if (ActiveTool == Tool.WallAdd && _prevNodeId != null)
            {
                CancelWallChain();
                _rightTarget = null;
            }
            else
            {
                _rightTarget = HitObject(_mouseWorld);
            }
            _drag = DragMode.PanView;
            _dragStartWorld = new Pt(sx, sy);
            return;
        }
        if (button == 1) // middle button: pan
        {
            _drag = DragMode.PanView;
            _dragStartWorld = new Pt(sx, sy);
            return;
        }
        if (button != 0) return;

        // transform handles take priority when something is selected
        if (Selected != null && ActiveTool == Tool.Edit)
        {
            var h = HitHandle(sx, sy);
            if (h.HasValue)
            {
                BeginHandleDrag(h.Value, sx, sy);
                return;
            }
        }

        Pt world = _mouseWorld;
        Pt snapped = SnapWorld(world);

        var fur = HitFurniture(world);
        var node = HitNode(world);
        var wall = node == null ? HitWall(world) : null;

        switch (ActiveTool)
        {
            case Tool.View:
                _drag = DragMode.PanView;
                _dragStartWorld = new Pt(sx, sy);
                break;

            case Tool.WallAdd:
            {
                if (WallsLocked) { Notify("Walls are locked"); break; }
                var tgt = ResolveWallTarget(world);
                if (tgt.Node != null)
                {
                    if (_prevNodeId != null && _prevNodeId != tgt.Node.Id) PushUndo();
                    WallStep(tgt.Node);
                }
                else
                {
                    // hit-test the resolved snap target, not the raw cursor — so the
                    // committed point matches the preview even when the cursor grazes a wall
                    var hitWall = HitWall(tgt.Point);
                    if (hitWall != null)
                    {
                        PushUndo();
                        var nn = Plan.Current.AddNodeToWall(hitWall, tgt.Point);
                        if (nn != null) WallStep(nn);
                    }
                    else if (CheckStep(tgt.Point))
                    {
                        PushUndo();
                        var created = Plan.Current.Seq.AddNode(tgt.Point.X, tgt.Point.Y);
                        WallStep(created);
                    }
                }
                Notify();
            }
                break;

            case Tool.Edit:
                if (fur != null)
                {
                    if (Selected != null) { Selected = null; }
                    else { Selected = fur; }
                    Notify();
                }
                else if (node != null)
                {
                    if (WallsLocked) { Notify("Walls are locked"); break; }
                    PushUndo();
                    _drag = DragMode.Node;
                    _dragNodeId = node.Id;
                }
                else if (wall != null)
                {
                    if (WallsLocked) { Notify("Walls are locked"); break; }
                    PushUndo();
                    _drag = DragMode.Wall;
                    _dragWall = wall;
                    _dragStartWorld = world;
                    _dragStartLeft = new Pt(wall.LeftNode.X, wall.LeftNode.Y);
                    _dragStartRight = new Pt(wall.RightNode.X, wall.RightNode.Y);
                }
                break;

            case Tool.Remove:
            {
                var rl = HitRoomLabel(world);
                if (rl != null) { PushUndo(); Plan.Current.RoomLabels.Remove(rl); Notify("Room label removed"); break; }
                if (fur != null) { PushUndo(); Plan.Current.RemoveFurniture(fur.Id); if (Selected == fur) Selected = null; Notify(); }
                else if (node != null)
                {
                    if (WallsLocked) { Notify("Walls are locked"); break; }
                    int before = _undo.Count;
                    PushUndo();
                    bool ok = Plan.Current.Seq.RemoveNode(node.Id);
                    if (!ok && _undo.Count > before) _undo.RemoveAt(_undo.Count - 1);
                    Notify(ok ? null : Plan.Current.Seq.LastError);
                }
                else if (wall != null)
                {
                    if (WallsLocked) { Notify("Walls are locked"); break; }
                    PushUndo();
                    Plan.Current.Seq.RemoveWall(wall.LeftNode.Id, wall.RightNode.Id);
                    Plan.Current.Seq.PruneIsolatedNodes();
                    Notify();
                }
                break;
            }

            case Tool.RoomLabel:
                // placement is driven by the UI layer (needs a name prompt);
                // see TryRoomLabelSpot / AddRoomLabel
                break;

            case Tool.Measure:
                _measureStart = snapped;
                _measureCur = snapped;
                _drag = DragMode.Measure;
                break;

            case Tool.FurnitureAddWindow:
            case Tool.FurnitureAddDoor:
                if (wall != null)
                {
                    PushUndo();
                    var item = ActiveTool == Tool.FurnitureAddDoor ? Catalog.Door : Catalog.Window;
                    Pt local = wall.LocalMatrix().Invert().Apply(world);
                    double lx = Math.Clamp(local.X, 0, Math.Max(0, wall.Length - item.Width * Constants.METER));
                    var f = Plan.AddFurniture(item, wall, new Pt(lx, 0));
                    f.ZIndex = (int)NextZ();
                    Notify($"Added {item.Name}");
                }
                break;
        }
    }

    public void PointerMove(double sx, double sy)
    {
        _mouseWorld = Vp.ScreenToWorld(sx, sy);

        switch (_drag)
        {
            case DragMode.PanView:
                Vp.PanBy(sx - _dragStartWorld.X, sy - _dragStartWorld.Y);
                _dragStartWorld = new Pt(sx, sy);
                if (_rightDown &&
                    Math.Abs(sx - _rightDownX) + Math.Abs(sy - _rightDownY) > 4)
                    _rightTarget = null; // it's a pan, not a click
                Changed?.Invoke();
                return;

            case DragMode.Node:
            {
                var n = Plan.Current.Seq.Nodes.GetValueOrDefault(_dragNodeId);
                if (n != null)
                {
                    Pt w = SnapWorld(_mouseWorld);
                    n.X = w.X;
                    n.Y = w.Y;
                    Plan.Current.Seq.Recompute();
                }
                Changed?.Invoke();
                return;
            }

            case DragMode.Wall when _dragWall != null:
            {
                double dx = _mouseWorld.X - _dragStartWorld.X;
                double dy = _mouseWorld.Y - _dragStartWorld.Y;
                _dragWall.LeftNode.X = _dragStartLeft.X + dx;
                _dragWall.LeftNode.Y = _dragStartLeft.Y + dy;
                _dragWall.RightNode.X = _dragStartRight.X + dx;
                _dragWall.RightNode.Y = _dragStartRight.Y + dy;
                Plan.Current.Seq.Recompute();
                Changed?.Invoke();
                return;
            }

            case DragMode.Handle when Selected != null:
                ApplyHandle(sx, sy);
                Changed?.Invoke();
                return;

            case DragMode.Measure:
                _measureCur = SnapWorld(_mouseWorld);
                Changed?.Invoke();
                return;
        }

        // not dragging: track what's under the cursor for hover-delete + highlight
        _hoverFur = HitFurniture(_mouseWorld);
        _hoverNode = _hoverFur == null ? HitNode(_mouseWorld) : null;
        _hoverWall = (_hoverFur == null && _hoverNode == null) ? HitWall(_mouseWorld) : null;

        Changed?.Invoke(); // refresh pointer indicator / wall preview
    }

    public void PointerUp(int button, double sx, double sy)
    {
        if (button == 2 && _rightDown)
        {
            _rightDown = false;
            _drag = DragMode.None;
            bool moved = Math.Abs(sx - _rightDownX) + Math.Abs(sy - _rightDownY) > 4;
            if (!moved && _rightTarget != null)
            {
                if (_rightTarget is Wall w)
                {
                    if (WallsLocked) Notify("Walls are locked");
                    else { PushUndo(); w.SetType(CurrentWallType); Notify($"Wall set to {WallTypeName(CurrentWallType)}"); }
                }
                else if (_rightTarget is Furniture f) { PushUndo(); f.CycleOrientation(); Notify(); }
            }
            _rightTarget = null;
            return;
        }

        if (_drag == DragMode.Measure) _measureStart = null;
        _drag = DragMode.None;
        _dragWall = null;
        _rightDown = false;
        _rightTarget = null;
        Changed?.Invoke();
    }

    public void Wheel(double deltaY, double sx, double sy)
    {
        double factor = deltaY < 0 ? 1.12 : 1 / 1.12;
        Vp.ZoomAt(sx, sy, factor);
        Changed?.Invoke();
    }

    public void KeySave() => Notify("Tip: use the Save button to download the plan");

    // ===== room labels (placement driven by UI for the name prompt) =====
    public bool PointInAnyRoom(double sx, double sy)
        => Rooms.RoomContaining(Plan.Current.Seq, Vp.ScreenToWorld(sx, sy)) != null;

    public void AddRoomLabelAt(double sx, double sy, string name)
    {
        Pt world = Vp.ScreenToWorld(sx, sy);
        if (Rooms.RoomContaining(Plan.Current.Seq, world) == null)
        {
            Notify("Click inside a closed room");
            return;
        }
        PushUndo();
        Plan.AddRoomLabel(world, string.IsNullOrWhiteSpace(name) ? "Room" : name.Trim());
        Notify("Room label added");
    }

    private RoomLabel? HitRoomLabel(Pt world)
    {
        double r = 36;
        RoomLabel? best = null;
        double bestD = double.MaxValue;
        foreach (var rl in Plan.Current.RoomLabels)
        {
            double d = Geometry.EuclideanDistance(world.X, rl.X, world.Y, rl.Y);
            if (d < r && d < bestD) { best = rl; bestD = d; }
        }
        return best;
    }

    public void CancelWallChain()
    {
        if (_prevNodeId != null || _wallPreviewStart != null)
        {
            _prevNodeId = null;
            _wallPreviewStart = null;
            Notify("Wall cancelled");
        }
    }

    // ===== wall drawing assist: magnet to corners + ortho/45 snapping =====
    private const double NODE_SNAP_PX = 18;   // screen-pixel magnet radius to existing nodes
    private const double ORTHO_TOL_DEG = 16;   // angle window that forces a straight H/V wall
    private const double DIAG_TOL_DEG = 9;     // angle window that forces a 45° wall

    public struct WallTarget
    {
        public WallNode? Node;
        public Pt Point;
    }

    // snap guides activated by the most recent ResolveWallTarget call — rendered as faint lines
    private readonly List<(Pt origin, Pt dir)> _activeGuides = new();
    public IReadOnlyList<(Pt origin, Pt dir)> ActiveGuides => _activeGuides;

    private double SnapVal(double v) => Snap ? Geometry.Snap(v) : Math.Truncate(v);

    public WallTarget ResolveWallTarget(Pt world)
    {
        _activeGuides.Clear();
        var seq = Plan.Current.Seq;

        // 1. magnet to the nearest existing node (corner / wall end)
        double r = NODE_SNAP_PX / Math.Max(0.0001, Vp.Scale);
        WallNode? best = null;
        double bestD = r;
        foreach (var n in seq.Nodes.Values)
        {
            if (_prevNodeId == n.Id) continue; // don't magnet onto the chain's own last node
            double d = Geometry.EuclideanDistance(world.X, n.X, world.Y, n.Y);
            if (d <= bestD) { bestD = d; best = n; }
        }
        if (best != null) return new WallTarget { Node = best, Point = new Pt(best.X, best.Y) };

        // 2. wall-endpoint snap guides: at each endpoint, a "cross" of the wall's
        //    direction and its perpendicular — makes parallel + equal-length walls easy
        var guides = CollectWallGuides(seq);
        double gtol = NODE_SNAP_PX / Math.Max(0.0001, Vp.Scale);

        // 3. angle snapping relative to the chain's previous node, combined with guides
        if (_prevNodeId != null && seq.Nodes.TryGetValue(_prevNodeId.Value, out var prev))
        {
            double dx = world.X - prev.X;
            double dy = world.Y - prev.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist > 1)
            {
                double deg = Math.Atan2(dy, dx) * 180.0 / Math.PI; // -180..180
                double n360 = (deg % 360 + 360) % 360;

                double ortho = Math.Round(n360 / 90.0) * 90.0;
                double dOrtho = Math.Abs(((n360 - ortho + 540) % 360) - 180);
                double diag = Math.Round(n360 / 45.0) * 45.0;
                double dDiag = Math.Abs(((n360 - diag + 540) % 360) - 180);

                Pt? chainDir = null;
                bool isOrtho = false;
                if (dOrtho <= ORTHO_TOL_DEG)
                {
                    double rad = ortho * Math.PI / 180.0;
                    chainDir = new Pt(Math.Cos(rad), Math.Sin(rad));
                    isOrtho = true;
                }
                else if (dDiag <= DIAG_TOL_DEG)
                {
                    double rad = diag * Math.PI / 180.0;
                    chainDir = new Pt(Math.Cos(rad), Math.Sin(rad));
                }

                if (chainDir != null)
                {
                    // snap to intersection of chain ray with a guide line, if any is close to the cursor
                    Pt prevPt = new(prev.X, prev.Y);
                    Pt? hit = null;
                    (Pt o, Pt d) bestG = default;
                    double bestHitD = gtol;
                    foreach (var g in guides)
                    {
                        if (!TryIntersectLines(prevPt, chainDir.Value, g.origin, g.dir, out var X)) continue;
                        double d = Geometry.EuclideanDistance(world.X, X.X, world.Y, X.Y);
                        if (d <= bestHitD) { bestHitD = d; hit = X; bestG = g; }
                    }
                    if (hit != null)
                    {
                        _activeGuides.Add(bestG);
                        return new WallTarget { Point = hit.Value };
                    }

                    if (isOrtho)
                    {
                        bool horizontal = ((int)Math.Round(ortho / 90.0)) % 2 == 0;
                        return horizontal
                            ? new WallTarget { Point = new Pt(SnapVal(world.X), prev.Y) }
                            : new WallTarget { Point = new Pt(prev.X, SnapVal(world.Y)) };
                    }
                    else
                    {
                        double len = SnapVal((Math.Abs(dx) + Math.Abs(dy)) / 2.0);
                        double sx = dx >= 0 ? 1 : -1;
                        double sy = dy >= 0 ? 1 : -1;
                        return new WallTarget { Point = new Pt(prev.X + sx * len, prev.Y + sy * len) };
                    }
                }
            }
        }
        else
        {
            // not chaining: project cursor onto the nearest guide line if close enough
            (Pt o, Pt d)? bestG = null;
            Pt bestProj = default;
            double bestProjD = gtol;
            foreach (var g in guides)
            {
                Pt P = ProjectOntoLine(world, g.origin, g.dir);
                double d = Geometry.EuclideanDistance(world.X, P.X, world.Y, P.Y);
                if (d <= bestProjD) { bestProjD = d; bestProj = P; bestG = g; }
            }
            if (bestG != null)
            {
                _activeGuides.Add(bestG.Value);
                return new WallTarget { Point = bestProj };
            }
        }

        // 4. plain grid snap
        return new WallTarget { Point = new Pt(SnapVal(world.X), SnapVal(world.Y)) };
    }

    private List<(Pt origin, Pt dir)> CollectWallGuides(WallNodeSequence seq)
    {
        var list = new List<(Pt, Pt)>();
        foreach (var w in seq.Walls)
        {
            Pt along = new(Math.Cos(w.ThetaRad), Math.Sin(w.ThetaRad));
            Pt perp = new(-along.Y, along.X);
            AddGuidesForNode(list, w.LeftNode, along, perp);
            AddGuidesForNode(list, w.RightNode, along, perp);
        }
        return list;
    }

    private void AddGuidesForNode(List<(Pt, Pt)> list, WallNode n, Pt along, Pt perp)
    {
        if (_prevNodeId == n.Id) return; // guides through the chain's anchor add no info
        list.Add((new Pt(n.X, n.Y), along));
        list.Add((new Pt(n.X, n.Y), perp));
    }

    private static Pt ProjectOntoLine(Pt p, Pt origin, Pt dir)
    {
        double t = (p.X - origin.X) * dir.X + (p.Y - origin.Y) * dir.Y;
        return new Pt(origin.X + t * dir.X, origin.Y + t * dir.Y);
    }

    private static bool TryIntersectLines(Pt o1, Pt d1, Pt o2, Pt d2, out Pt result)
    {
        double det = d1.X * (-d2.Y) - d1.Y * (-d2.X);
        if (Math.Abs(det) < 1e-9) { result = default; return false; }
        double dx = o2.X - o1.X, dy = o2.Y - o1.Y;
        double t = (dx * (-d2.Y) - dy * (-d2.X)) / det;
        result = new Pt(o1.X + t * d1.X, o1.Y + t * d1.Y);
        return true;
    }

    public Pt WallPreviewPoint() => ResolveWallTarget(_mouseWorld).Point;

    // ===== wall-add chain (mirrors AddWallManager) =====
    private bool CheckStep(Pt coords)
    {
        if (_prevNodeId == null)
        {
            foreach (var n in Plan.Current.Seq.Nodes.Values)
                if (Geometry.EuclideanDistance(coords.X, n.X, coords.Y, n.Y) < 0.3 * Constants.METER)
                    return false;
            return true;
        }
        var prev = Plan.Current.Seq.Nodes[_prevNodeId.Value];
        return Geometry.EuclideanDistance(coords.X, prev.X, coords.Y, prev.Y) >= 0.3 * Constants.METER;
    }

    private void WallStep(WallNode node)
    {
        if (_prevNodeId == null)
        {
            _prevNodeId = node.Id;
            _wallPreviewStart = new Pt(node.X, node.Y);
            return;
        }
        if (_prevNodeId == node.Id)
        {
            _prevNodeId = null;
            _wallPreviewStart = null;
            return;
        }
        var w = Plan.Current.Seq.AddWall(_prevNodeId.Value, node.Id);
        w?.SetType(CurrentWallType);
        _prevNodeId = node.Id;
        _wallPreviewStart = new Pt(node.X, node.Y);
    }

    private Pt SnapWorld(Pt p)
        => Snap ? new Pt(Geometry.Snap(p.X), Geometry.Snap(p.Y)) : new Pt(Math.Truncate(p.X), Math.Truncate(p.Y));

    // ===== hit testing =====
    private object? HitObject(Pt world)
        => (object?)HitFurniture(world) ?? HitNode(world) ?? (object?)HitWall(world);

    private Furniture? HitFurniture(Pt world)
    {
        Furniture? best = null;
        foreach (var f in Plan.Current.FurnitureMap.Values)
            if (f.HitTest(world) && (best == null || f.ZIndex >= best.ZIndex))
                best = f;
        return best;
    }

    private WallNode? HitNode(Pt world)
    {
        foreach (var n in Plan.Current.Seq.Nodes.Values)
        {
            double half = n.Size / 2 + 2;
            if (Math.Abs(world.X - n.X) <= half && Math.Abs(world.Y - n.Y) <= half)
                return n;
        }
        return null;
    }

    private Wall? HitWall(Pt world)
    {
        foreach (var w in Plan.Current.Seq.Walls)
            if (w.HitTest(world)) return w;
        return null;
    }

    // ===== transform handles =====
    public IEnumerable<(HandleType type, double x, double y)> HandlePositions()
    {
        if (Selected == null || ActiveTool != Tool.Edit) yield break;
        var (cx, cy, ux, uy, vx, vy, hw, hh) = SelectedScreenBox();

        // centre + half axes (screen)
        (double, double) P(double a, double b) => (cx + ux * a + vx * b, cy + uy * a + vy * b);

        var (mx, my) = P(0, 0);
        yield return (HandleType.Move, mx, my);

        if (Selected.IsAttached)
        {
            // attached windows can be stretched along the wall
            var (rx2, ry2) = P(hw, 0);
            yield return (HandleType.Horizontal, rx2, ry2);
            yield break;
        }

        var (rx, ry) = P(hw, 0);
        yield return (HandleType.Horizontal, rx, ry);
        var (bx, by) = P(0, hh);
        yield return (HandleType.Vertical, bx, by);
        var (gx, gy) = P(hw, hh);
        yield return (HandleType.Corner, gx, gy);

        // rotate grip: just outside the top-right corner, fixed screen offset
        var (cornerX, cornerY) = P(hw, -hh); // top-right
        double dlen = Math.Sqrt((ux - vx) * (ux - vx) + (uy - vy) * (uy - vy));
        double dirX = dlen > 1e-6 ? (ux - vx) / dlen : 0.707;
        double dirY = dlen > 1e-6 ? (uy - vy) / dlen : -0.707;
        const double ROT_OFFSET = 22;
        yield return (HandleType.Rotate,
            cornerX + dirX * ROT_OFFSET, cornerY + dirY * ROT_OFFSET);
    }

    // returns centre, unit-ish axis vectors scaled to half extents, and half sizes
    private (double cx, double cy, double ux, double uy, double vx, double vy, double hw, double hh) SelectedScreenBox()
    {
        var f = Selected!;
        Mat m = Vp.BaseMatrix.Mul(f.LocalMatrix());
        Pt p0 = m.Apply(new Pt(0, 0));
        Pt px = m.Apply(new Pt(f.Width, 0));
        Pt py = m.Apply(new Pt(0, f.Height));
        Pt pc = m.Apply(new Pt(f.Width / 2, f.Height / 2));
        double ux = (px.X - p0.X) / 2, uy = (px.Y - p0.Y) / 2;
        double vx = (py.X - p0.X) / 2, vy = (py.Y - p0.Y) / 2;
        return (pc.X, pc.Y, ux, uy, vx, vy, 1, 1);
    }

    private HandleType? HitHandle(double sx, double sy)
    {
        foreach (var (type, x, y) in HandlePositions())
            if (Math.Abs(sx - x) <= 9 && Math.Abs(sy - y) <= 9)
                return type;
        return null;
    }

    private void BeginHandleDrag(HandleType h, double sx, double sy)
    {
        PushUndo();
        _drag = DragMode.Handle;
        _dragHandle = h;
        var f = Selected!;
        _hStartScaleX = f.ScaleX;
        _hStartScaleY = f.ScaleY;
        _hStartRot = f.Rotation;
        _hStartLocalX = f.X;
        // rotation pivots on the configured pivot point (= (f.X, f.Y) by construction);
        // resize uses the box centre as the scale anchor
        Pt cw = h == HandleType.Rotate ? new Pt(f.X, f.Y) : SelectedCenterWorld();
        _hStartCenter = cw;
        Pt mw = Vp.ScreenToWorld(sx, sy);
        _hStartDist = Math.Max(1, Geometry.EuclideanDistance(mw.X, cw.X, mw.Y, cw.Y));
        _hStartAngle = Math.Atan2(mw.Y - cw.Y, mw.X - cw.X);
        f.ZIndex = (int)NextZ();
    }

    private Pt SelectedCenterWorld()
    {
        var f = Selected!;
        return f.LocalMatrix().Apply(new Pt(f.Width / 2, f.Height / 2));
    }

    private void ApplyHandle(double sx, double sy)
    {
        var f = Selected!;
        Pt mw = Vp.ScreenToWorld(sx, sy);

        switch (_dragHandle)
        {
            case HandleType.Move:
                if (f.IsAttached && f.AttachedTo != null)
                {
                    Pt local = f.AttachedTo.LocalMatrix().Invert().Apply(mw);
                    double effW = f.Width * Math.Abs(f.ScaleX);
                    double minX = (effW - f.Width) / 2;
                    double maxX = f.AttachedTo.Length - (f.Width + effW) / 2;
                    f.X = Math.Clamp(local.X - f.Width / 2, minX, Math.Max(minX, maxX));
                }
                else
                {
                    // keep the box centre under the cursor; (f.X, f.Y) is the pivot in world
                    double dxL = f.Width / 2 - f.PivotX;
                    double dyL = f.Height / 2 - f.PivotY;
                    double cos = Math.Cos(f.Rotation);
                    double sin = Math.Sin(f.Rotation);
                    double dxW = cos * f.ScaleX * dxL - sin * f.ScaleY * dyL;
                    double dyW = sin * f.ScaleX * dxL + cos * f.ScaleY * dyL;
                    Pt center = SnapWorld(mw);
                    f.X = center.X - dxW;
                    f.Y = center.Y - dyW;
                }
                break;

            case HandleType.Rotate:
            {
                double ang = Math.Atan2(mw.Y - _hStartCenter.Y, mw.X - _hStartCenter.X);
                double rot = _hStartRot + (ang - _hStartAngle);

                // all furniture snaps to whole degrees; 45° multiples are "sticky"
                double deg = rot * 180.0 / Math.PI;
                double nearest45 = Math.Round(deg / 45.0) * 45.0;
                double dTo45 = Math.Abs(((deg - nearest45 + 540) % 360) - 180);
                deg = dTo45 <= 6.0 ? nearest45 : Math.Round(deg);
                rot = deg * Math.PI / 180.0;

                double heading = ((deg % 360) + 360) % 360;
                Notification = f.IsCompass ? $"Compass: {heading:0}°" : $"{heading:0}°";

                f.Rotation = rot;
                break;
            }

            case HandleType.Horizontal:
            case HandleType.Vertical:
            case HandleType.Corner:
            {
                double dist = Math.Max(1, Geometry.EuclideanDistance(mw.X, _hStartCenter.X, mw.Y, _hStartCenter.Y));
                double ratio = dist / _hStartDist;
                // doors scale uniformly — width and swing radius stay in proportion
                bool uniform = f.IsDoor;
                if (_dragHandle != HandleType.Vertical || uniform)
                    f.ScaleX = Math.Sign(_hStartScaleX == 0 ? 1 : _hStartScaleX) * Math.Max(0.1, Math.Abs(_hStartScaleX) * ratio);
                if (_dragHandle != HandleType.Horizontal || uniform)
                    f.ScaleY = Math.Sign(_hStartScaleY == 0 ? 1 : _hStartScaleY) * Math.Max(0.1, Math.Abs(_hStartScaleY) * ratio);

                // attached window: keep the visible rect within the wall length and reposition
                // X if the stretch would push the visible edge past the wall ends
                if (f.IsAttached && f.AttachedTo != null)
                {
                    double maxScale = f.AttachedTo.Length / Math.Max(1, f.Width);
                    if (Math.Abs(f.ScaleX) > maxScale)
                    {
                        f.ScaleX = Math.Sign(f.ScaleX) * maxScale;
                        if (uniform) f.ScaleY = Math.Sign(f.ScaleY) * maxScale;
                    }
                    double effW = f.Width * Math.Abs(f.ScaleX);
                    double minX = (effW - f.Width) / 2;
                    double maxX = f.AttachedTo.Length - (f.Width + effW) / 2;
                    f.X = Math.Clamp(f.X, minX, Math.Max(minX, maxX));
                }
                break;
            }
        }
    }
}
