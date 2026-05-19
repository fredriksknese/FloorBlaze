namespace FloorPlan.Editor;

public static class Constants
{
    // how many pixels is a meter
    public const double METER = 100;
    public const double WALL_THICKNESS = 0.2 * METER;
    public const double INTERIOR_WALL_THICKNESS = 0.1 * METER;
    public const double ROOM_DIVIDER_THICKNESS = 0.07 * METER;
    public const double LABEL_OFFSET = 10;

    public const double WORLD_SIZE = 50 * METER;

    public const double MIN_SCALE = 0.25;
    public const double MAX_SCALE = 6.0;
}

public enum Tool
{
    WallAdd,
    FurnitureAdd,
    Edit,
    Remove,
    Measure,
    FurnitureAddWindow,
    FurnitureAddDoor,
    View,
    RoomLabel
}

// Interior is 0 so it stays the default (matches the old exterior=false default)
public enum WallType
{
    Interior,
    Exterior,
    RoomDivider
}

public enum StairKind
{
    None,
    Straight,
    LShaped,
    Spiral
}

public enum HandleType
{
    Horizontal,
    Vertical,
    Corner,
    Rotate,
    Move
}
