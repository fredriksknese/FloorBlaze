using FloorPlan.Editor.Persistence;

namespace FloorPlan.Editor.Model;

public class WallNodeSequence
{
    private readonly Dictionary<int, WallNode> _nodes = new();
    private readonly Dictionary<int, List<int>> _links = new();
    private readonly List<Wall> _walls = new();
    private static int _wallNodeId;

    public string? LastError { get; private set; }

    public static void ResetIdCounter() => _wallNodeId = 0;
    public void SetId(int id) => _wallNodeId = id;
    public int GetWallNodeId() => _wallNodeId;
    public int GetNewNodeId() => ++_wallNodeId;

    public bool Contains(int id) => _nodes.ContainsKey(id);
    public IReadOnlyList<Wall> Walls => _walls;
    public IReadOnlyDictionary<int, WallNode> Nodes => _nodes;
    public IReadOnlyDictionary<int, List<int>> Links => _links;
    public IEnumerable<Wall> ExteriorWalls => _walls.Where(w => w.IsExterior);

    public WallNode AddNode(double x, double y, int? id = null)
    {
        int nodeId = id ?? GetNewNodeId();
        var node = new WallNode(x, y, nodeId);
        _nodes[nodeId] = node;
        _links[nodeId] = new List<int>();
        return node;
    }

    public Wall? AddWall(int leftId, int rightId)
    {
        if (leftId == rightId) return null;
        if (leftId > rightId) (leftId, rightId) = (rightId, leftId);
        if (_links.TryGetValue(leftId, out var l) && l.Contains(rightId)) return null;
        if (!_nodes.ContainsKey(leftId) || !_nodes.ContainsKey(rightId)) return null;

        _links[leftId].Add(rightId);
        var wall = new Wall(_nodes[leftId], _nodes[rightId]);
        _walls.Add(wall);
        return wall;
    }

    public void RemoveWall(int leftId, int rightId)
    {
        if (leftId > rightId) (leftId, rightId) = (rightId, leftId);
        if (_links.TryGetValue(leftId, out var l))
            l.Remove(rightId);
        _walls.RemoveAll(w => w.LeftNode.Id == leftId && w.RightNode.Id == rightId
                           || w.LeftNode.Id == rightId && w.RightNode.Id == leftId);
    }

    public bool RemoveNode(int id)
    {
        LastError = null;
        bool isolated = true;
        if (_links.TryGetValue(id, out var outs) && outs.Count > 0)
            isolated = false;
        else
        {
            foreach (var kv in _links)
                if (kv.Value.Contains(id)) { isolated = false; break; }
        }

        if (!isolated)
        {
            LastError = "Cannot delete node with walls attached. Remove walls first.";
            return false;
        }
        _nodes.Remove(id);
        _links.Remove(id);
        return true;
    }

    public Wall? GetWall(int leftId, int rightId)
    {
        return _walls.FirstOrDefault(w =>
            (w.LeftNode.Id == leftId && w.RightNode.Id == rightId) ||
            (w.LeftNode.Id == rightId && w.RightNode.Id == leftId));
    }

    public void Recompute()
    {
        foreach (var w in _walls) w.Recompute();
    }

    public void Reset()
    {
        _nodes.Clear();
        _links.Clear();
        _walls.Clear();
        _wallNodeId = 0;
    }

    public void Load(List<NodeSerializable> nodes, List<WallSerializable> walls)
    {
        foreach (var n in nodes) AddNode(n.x, n.y, n.id);
        foreach (var w in walls)
        {
            var wall = AddWall(w.left, w.right);
            if (wall == null) continue;
            WallType t = w.type != 0 ? (WallType)w.type : (w.exterior ? WallType.Exterior : WallType.Interior);
            wall.SetType(t);
        }
    }
}
