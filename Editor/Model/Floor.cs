using FloorPlan.Editor.Persistence;

namespace FloorPlan.Editor.Model;

public class Floor
{
    public WallNodeSequence Seq { get; } = new();
    public Dictionary<int, Furniture> FurnitureMap { get; } = new();
    public List<RoomLabel> RoomLabels { get; } = new();

    public Floor() { }

    // clone exterior walls from a previous floor
    public Floor(Floor previous)
    {
        var map = new Dictionary<int, int>();
        foreach (var wall in previous.Seq.ExteriorWalls)
        {
            foreach (var node in new[] { wall.LeftNode, wall.RightNode })
            {
                if (!map.ContainsKey(node.Id))
                {
                    int newId = Seq.GetNewNodeId();
                    map[node.Id] = newId;
                    Seq.AddNode(node.X, node.Y, newId);
                }
            }
        }
        foreach (var wall in previous.Seq.ExteriorWalls)
        {
            var nw = Seq.AddWall(map[wall.LeftNode.Id], map[wall.RightNode.Id]);
            nw?.SetType(WallType.Exterior);
        }
    }

    public Floor(FloorSerializable data)
    {
        Seq.Load(data.wallNodes, data.walls);
        foreach (var f in data.furnitureArray)
        {
            var item = Catalog.Get(f.texturePath)
                ?? (f.texturePath == "door" ? Catalog.Door
                  : f.texturePath == "window" ? Catalog.Window
                  : new CatalogItem(f.texturePath, f.texturePath, "_", 1, 1, "#999", "?"));
            var fur = new Furniture(item, f.id)
            {
                Width = f.width * Constants.METER,
                Height = f.height * Constants.METER,
                X = f.x,
                Y = f.y,
                Rotation = f.rotation,
                Orientation = f.orientation,
                ZIndex = f.zIndex == 0 ? 1 : f.zIndex,
            };
            if (f.attachedToLeft != 0 || f.attachedToRight != 0)
            {
                fur.AttachedTo = Seq.GetWall(f.attachedToLeft, f.attachedToRight);
                fur.IsAttached = fur.AttachedTo != null;
                fur.AttachedToLeft = f.attachedToLeft;
                fur.AttachedToRight = f.attachedToRight;
            }
            fur.CycleOrientationTo(f.orientation);
            FurnitureMap[f.id] = fur;
        }
        foreach (var r in data.roomLabels)
            RoomLabels.Add(new RoomLabel(r.id, r.x, r.y, r.name));
    }

    public Furniture AddFurniture(CatalogItem item, int id, Wall? attachedTo, Pt coords)
    {
        var fur = new Furniture(item, id);
        if (attachedTo != null)
        {
            fur.IsAttached = true;
            fur.AttachedTo = attachedTo;
            fur.AttachedToLeft = attachedTo.LeftNode.Id;
            fur.AttachedToRight = attachedTo.RightNode.Id;
            fur.X = coords.X;
            if (fur.Key == "window")
            {
                fur.Height = attachedTo.Thickness;
                fur.Y = attachedTo.Thickness / 2 - fur.Height / 2;
            }
            else if (fur.Key == "door")
            {
                // door SVG is 100x100 (square) — keep catalog W = H so the swing radius
                // equals the door's wall length. The matrix anchors Y on the wall centreline.
                fur.Y = 0;
            }
        }
        else
        {
            fur.X = coords.X;
            fur.Y = coords.Y;
        }
        FurnitureMap[id] = fur;
        return fur;
    }

    public void RemoveFurniture(int id) => FurnitureMap.Remove(id);

    // split a wall by inserting a node on it
    public WallNode? AddNodeToWall(Wall wall, Pt coords)
    {
        int leftId = wall.LeftNode.Id;
        int rightId = wall.RightNode.Id;

        if (Math.Abs(wall.ThetaDeg - 90) > 0.001 && Math.Abs(wall.ThetaDeg - 270) > 0.001)
            coords.Y = Geometry.GetCorrespondingY(coords.X,
                new Pt(wall.LeftNode.X, wall.LeftNode.Y),
                new Pt(wall.RightNode.X, wall.RightNode.Y));

        if (Math.Abs(Geometry.EuclideanDistance(coords.X, wall.LeftNode.X, coords.Y, wall.LeftNode.Y)) < 0.2 * Constants.METER)
            return null;
        if (Math.Abs(Geometry.EuclideanDistance(coords.X, wall.RightNode.X, coords.Y, wall.RightNode.Y)) < 0.2 * Constants.METER)
            return null;

        WallType wasType = wall.Type;
        Seq.RemoveWall(leftId, rightId);
        var newNode = Seq.AddNode(coords.X, coords.Y);
        var w1 = Seq.AddWall(leftId, newNode.Id);
        var w2 = Seq.AddWall(newNode.Id, rightId);
        w1?.SetType(wasType);
        w2?.SetType(wasType);
        return newNode;
    }

    public FloorSerializable Serialize()
    {
        var fs = new FloorSerializable();
        foreach (var n in Seq.Nodes.Values)
            fs.wallNodes.Add(new NodeSerializable { id = n.Id, x = n.X, y = n.Y });
        foreach (var w in Seq.Walls)
            fs.walls.Add(new WallSerializable
            {
                left = w.LeftNode.Id,
                right = w.RightNode.Id,
                exterior = w.IsExterior,
                type = (int)w.Type
            });
        foreach (var f in FurnitureMap.Values)
            fs.furnitureArray.Add(new FurnitureSerializable
            {
                id = f.Id,
                texturePath = f.Key,
                width = f.Width / Constants.METER,
                height = f.Height / Constants.METER,
                rotation = f.Rotation,
                x = f.X,
                y = f.Y,
                orientation = f.Orientation,
                zIndex = f.ZIndex,
                attachedToLeft = f.AttachedToLeft,
                attachedToRight = f.AttachedToRight,
            });
        foreach (var r in RoomLabels)
            fs.roomLabels.Add(new RoomLabelSerializable { id = r.Id, x = r.X, y = r.Y, name = r.Name });
        return fs;
    }
}
