# FloorBlaze

A browser-based **2D floor-plan editor** written in **C# / Blazor WebAssembly**.
The entire editor — geometry, tools, hit-testing, transforms, room detection —
runs as C# in the browser; rendering is an immediate-mode scene drawn to an
HTML5 `<canvas>` through a thin JavaScript interop layer.

> Originally inspired by the React/PixiJS *arcada* editor, rebuilt from scratch
> in C# with no PixiJS and no server — it is fully self-contained.

---

## Features

### Walls
- Draw chained walls; click an existing node to join, double-click a node to end.
- Click a wall to **split** it with a new node.
- Three wall types — **Exterior**, **Interior**, **Room divider** — chosen from
  the **+** menu; right-click a wall to re-apply the current type.
- **Magnetic snapping**: snaps to existing corners/endpoints, and to
  **horizontal / vertical / 45°** so straight and right-angle walls are easy.
- **Wall-endpoint guides**: each existing endpoint emits an invisible "cross"
  of its wall's direction + perpendicular. The cursor snaps onto these,
  making **parallel walls of equal length** a one-click affair.
- **Live preview** — while drawing, the next wall is previewed at its real
  thickness and type, not a thin line.
- **Lock** 🔒 — freezes all walls (no move, delete, type change, or new walls).

### Rooms
- Automatic **room detection** from the wall graph (planar face finding).
- **Inside / outside differentiation** — every closed room gets a white floor
  fill so the inside reads clearly against the gray outside, no toggle needed.
- **Room labels** — add via the **+** menu, click inside a closed room, name it.
- **Room area** toggle 📏 — shades each detected room and shows its size in m².

### Furniture
- ~60 furniture items across Bedroom, Bathroom, Kitchen, Office, Living Room,
  Other and Structural categories, plus **windows** and a **compass**.
- Real **SVG icons**, rendered under each object's transform.
- Select, **move**, **resize**, and **rotate** via on-object handles.
- Rotation snaps to **1°** with **sticky 45°** stops; live degree readout.
- Windows attach to walls and slide along them.
- Right-click furniture to flip its orientation.

### Editing & navigation
- **Undo / Redo** — full-plan snapshots, via the navbar buttons or
  **Ctrl+Z** / **Ctrl+Y**.
- **Delete** key removes the selected item, or whatever is under the cursor
  (with a hover highlight showing the target).
- **Pan** with right-drag, middle-drag, or the View tool; **wheel** to zoom.
- **Arrow keys** pan the view, or nudge the selected item when one is selected.
- **Esc** deselects / cancels the current wall chain / closes menus.
- **Snap-to-grid** toggle and wall size labels.
- **Hotkeys**: **E** exterior wall, **I** interior wall, **R** room divider,
  **F** furniture, **W** window, **D** door, **L** room label.

### Plans
- **Multi-floor** support; new floors clone the exterior walls below them.
- **Save / Load** plans as JSON.
- **Save as PNG** — exports the whole floor (all walls, doors, windows,
  furniture and labels) with a 1 m margin, regardless of pan/zoom.
- **Autosave to the browser** — the current plan, active floor, and the full
  undo/redo history are written to `localStorage` on every change and
  restored on page reload, so a refresh never loses work.
- **Remote storage** — appending `?storage=<url>` to the page URL routes
  load and save through that endpoint instead of `localStorage`: a `GET` on
  startup, a `POST` (JSON body) on every change. When this mode is active,
  the manual Save (💾) and Load (⬆️) buttons are hidden, since persistence
  is handled automatically by the endpoint.

---

## Running locally

Requires the .NET SDK (net10.0).

```bash
dotnet run
```

Then open the printed `http://localhost:<port>` URL.

---

## Tech notes

- **Blazor WebAssembly** (`net10.0`), no backend.
- Editor model & logic: pure C# (`Editor/`).
- Rendering: C# builds a list of draw commands → `wwwroot/js/floorplan.js`
  paints them to a 2D canvas (supports transformed rects, lines, polygons,
  text, and cached SVG images).
- Furniture catalogue and SVG icons were imported from the original arcada
  backend and bundled under `wwwroot/furniture/` — no runtime dependency.

---

## Author

Built by **Fredrik Karlsson**.

- GitHub: <https://github.com/fredriksknese/>
- LinkedIn: <https://www.linkedin.com/in/fredrik-karlsson/>
