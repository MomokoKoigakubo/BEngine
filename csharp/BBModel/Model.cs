using System.Numerics;

namespace IdleL.BBModel;

class Resolution { public int Width, Height; }

class TextureMeta
{
    public int UvWidth = 16, UvHeight = 16;
    public float FrameTime = 1.0f;
    public string FlipType = "";
    public string Name = "";
}

class OutlinerNode
{
    public string Uuid = "";
    public bool IsGroup;
    public List<OutlinerNode> Children = new();
}

enum ElementType { Cube, Mesh, Locator, NullObject, Unknown }

class CubeFace
{
    public float U0, V0, U1, V1;
    public int Texture = -1;
    public int Rotation = 0;
    public bool Present = false;
}

class MeshFace
{
    public List<string> Vertices = new();
    public Dictionary<string, Vector2> Uv = new();
    public int Texture = -1;
}

class Element
{
    public string Uuid = "";
    public ElementType Type = ElementType.Cube;
    public Vector3 Origin, Rotation, Position;

    // cube
    public Vector3 From, To;
    public CubeFace North = new(), South = new(), East = new(), West = new(), Up = new(), Down = new();

    // mesh
    public Dictionary<string, Vector3> Vertices = new();
    public Dictionary<string, MeshFace> Faces = new();
}

class Group
{
    public string Uuid = "";
    public Vector3 Origin, Rotation;
    public List<string> Children = new();
}

class BBModelParts
{
    public Resolution Res = new();
    public List<Element> Elements = new();
    public List<Group> Groups = new();
    public List<OutlinerNode> Outliner = new();
    public bool EulerXYZ = false;       // euler order: false = ZYX (bedrock), true = XYZ (free/java)
    public List<TextureMeta> Textures = new();
    public List<Animation> Animations = new();
}
