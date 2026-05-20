namespace FloorPlan.Editor.Persistence;

public class NodeSerializable
{
    public int id { get; set; }
    public double x { get; set; }
    public double y { get; set; }
}

public class WallSerializable
{
    public int left { get; set; }
    public int right { get; set; }
    public bool exterior { get; set; }   // kept for back-compat
    public int type { get; set; }        // 0 interior, 1 exterior, 2 room divider
}

public class RoomLabelSerializable
{
    public int id { get; set; }
    public double x { get; set; }
    public double y { get; set; }
    public string name { get; set; } = "Room";
}

public class FurnitureSerializable
{
    public int id { get; set; }
    public string texturePath { get; set; } = "";
    public double width { get; set; }
    public double height { get; set; }
    public double rotation { get; set; }
    public double x { get; set; }
    public double y { get; set; }
    public int orientation { get; set; }
    public int zIndex { get; set; }
    public int attachedToLeft { get; set; }
    public int attachedToRight { get; set; }
}

public class FloorSerializable
{
    public List<NodeSerializable> wallNodes { get; set; } = new();
    public List<WallSerializable> walls { get; set; } = new();
    public List<FurnitureSerializable> furnitureArray { get; set; } = new();
    public List<RoomLabelSerializable> roomLabels { get; set; } = new();
}

public class FloorPlanSerializable
{
    public List<FloorSerializable> floors { get; set; } = new();
    public int furnitureId { get; set; }
    public int wallNodeId { get; set; }
    public int roomLabelId { get; set; }
}

public class HistoryEntrySerializable
{
    public string json { get; set; } = "";
    public int floor { get; set; }
}

public class FullStateSerializable
{
    public string plan { get; set; } = "";
    public int floor { get; set; }
    public List<HistoryEntrySerializable> undo { get; set; } = new();
    public List<HistoryEntrySerializable> redo { get; set; } = new();
}
