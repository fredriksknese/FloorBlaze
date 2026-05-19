using System.Text.Json;
using FloorPlan.Editor.Persistence;

namespace FloorPlan.Editor.Model;

public class FloorPlanModel
{
    private readonly List<Floor> _floors = new();
    public int CurrentIndex { get; private set; }
    public int FurnitureId { get; set; }
    public int RoomLabelId { get; set; }
    public bool LabelsVisible { get; set; } = true;
    public string? Notice { get; private set; }

    public FloorPlanModel()
    {
        _floors.Add(new Floor());
    }

    public Floor Current => _floors[CurrentIndex];
    public int FloorCount => _floors.Count;
    public int CurrentNumber => CurrentIndex;

    public void SetFloor(int i) => CurrentIndex = Math.Clamp(i, 0, _floors.Count - 1);

    public void ChangeFloor(int by)
    {
        int previous = CurrentIndex;
        int target = CurrentIndex + by;
        if (target < 0) return;
        CurrentIndex = target;
        while (_floors.Count <= CurrentIndex)
            _floors.Add(new Floor(_floors[previous]));
    }

    public bool RemoveFloor()
    {
        Notice = null;
        if (_floors.Count < 2)
        {
            Notice = "This is the only floor. A plan must have at least one floor.";
            return false;
        }
        int old = CurrentIndex;
        _floors.RemoveAt(old);
        CurrentIndex = Math.Max(0, old - 1);
        return true;
    }

    public Furniture AddFurniture(CatalogItem item, Wall? attachedTo, Pt coords)
    {
        FurnitureId += 1;
        return Current.AddFurniture(item, FurnitureId, attachedTo, coords);
    }

    public RoomLabel AddRoomLabel(Pt coords, string name)
    {
        RoomLabelId += 1;
        var rl = new RoomLabel(RoomLabelId, coords.X, coords.Y, name);
        Current.RoomLabels.Add(rl);
        return rl;
    }

    public string Save()
    {
        var fp = new FloorPlanSerializable
        {
            furnitureId = FurnitureId,
            wallNodeId = _floors[0].Seq.GetWallNodeId(),
            roomLabelId = RoomLabelId,
        };
        foreach (var f in _floors) fp.floors.Add(f.Serialize());
        return JsonSerializer.Serialize(fp, new JsonSerializerOptions { WriteIndented = true });
    }

    public void Load(string text)
    {
        var plan = JsonSerializer.Deserialize<FloorPlanSerializable>(text)
                   ?? throw new InvalidOperationException("Invalid plan file");
        _floors.Clear();
        CurrentIndex = 0;
        WallNodeSequence.ResetIdCounter();
        foreach (var fd in plan.floors)
            _floors.Add(new Floor(fd));
        if (_floors.Count == 0) _floors.Add(new Floor());
        FurnitureId = plan.furnitureId;
        RoomLabelId = plan.roomLabelId;
        _floors[0].Seq.SetId(plan.wallNodeId);
    }
}
