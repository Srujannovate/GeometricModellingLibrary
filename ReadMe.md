\# Geometric Modelling Library

A WPF application for 3D model viewing, creation, and manipulation powered by HelixToolkit and OpenCascade.

## Command Window

The application includes an interactive command window at the bottom of the main window. Type commands at the `>` prompt and press **Enter** to execute. Use **Up/Down** arrow keys to navigate command history.

### Create Commands

| Command | Description |
|---|---|
| `create cube [size]` | Create a cube centered at the origin. Default size is **2**. |
| `create cylinder [radius] [height]` | Create a cylinder centered at the origin with its axis along Y. Default radius is **1**, default height is **3**. |
| `create rectangle [width] [height] [depth]` | Create a rectangular box centered at the origin. Default dimensions are **3 × 2 × 1**. `create box` also works. |

Each created body is automatically named (e.g. `Cube_1`, `Cylinder_2`, `Rectangle_1`) and rendered in the 3D viewport with a distinct color.

### Transform Commands

All transform commands reference a body by name. Names are case-insensitive.

| Command | Description |
|---|---|
| `translate <name> dx,dy,dz` | Translate a body by the given offset. Example: `translate Cube_1 5,0,0` |
| `rotate <name> angle,ax,ay,az` | Rotate a body by `angle` degrees around the axis `(ax, ay, az)`. The axis is automatically normalized. Example: `rotate Cube_1 45,0,1,0` |
| `scale <name> sx,sy,sz` | Scale a body by the given factors along each axis. Example: `scale Cube_1 2,2,2` |

### Management Commands

| Command | Description |
|---|---|
| `list` | List all created bodies with their type and number of applied transforms. |
| `delete <name>` | Remove a body from the scene. `remove` also works. |

### File Commands

| Command | Description |
|---|---|
| `open [filepath]` | Open a 3D model file (`.obj`, `.step`, `.stp`). If no path is given, a file dialog appears. |
| `import [filepath]` | Load and execute a command schema file (`.gml`, `.txt`). If no path is given, a file dialog appears. |

### Application Commands

| Command | Description |
|---|---|
| `exit` | Close the application. `quit` also works. |
| `help` | Display a summary of all available commands. |
| `clear` | Clear the command output area. |

## Command Schema Files

A schema file is a plain-text file (`.gml` or `.txt`) containing one command per line. When imported, all commands execute sequentially. This lets you define reusable scenes.

- Empty lines are ignored.
- Lines starting with `#` or `//` are treated as comments.
- Every other line is executed as a normal command.

**Example schema** (`scene.gml`):

```
# Build a simple scene
create cube 2
create cylinder 0.5 4
create rectangle 4 1 2

# Position the shapes
translate Cube_1 -3,0,0
translate Cylinder_1 0,0,0
rotate Cylinder_1 90,1,0,0
translate Rectangle_1 3,0,0
```

Load it with:

```
> import D:\scenes\scene.gml
```

### Examples

```
> create cube
  Created 'Cube_1' (size=2.00)

> create cylinder 0.5 4
  Created 'Cylinder_1' (radius=0.50, height=4.00)

> translate Cube_1 3,0,0
  Translated 'Cube_1' by (3.00, 0.00, 0.00)

> rotate Cylinder_1 90,1,0,0
  Rotated 'Cylinder_1' by 90.0° around (1.00,0.00,0.00)

> create rectangle 4 1 2
  Created 'Rectangle_1' (width=4.00, height=1.00, depth=2.00)

> list
  Bodies (3):
    Cube_1 [Cube] transforms=1
    Cylinder_1 [Cylinder] transforms=1
    Rectangle_1 [Rectangle] transforms=0

> delete Cube_1
  Deleted 'Cube_1'
```
