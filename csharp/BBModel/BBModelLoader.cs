using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using IdleL.Molang;

namespace IdleL.BBModel;

static class BBModelLoader
{
    public static BBModelParts Load(string jsonText)
    {
        JsonNode root = JsonNode.Parse(jsonText)!;
        var parts = new BBModelParts();

        var res = root["resolution"]!;
        parts.Res.Width  = res["width"]!.GetValue<int>();
        parts.Res.Height = res["height"]!.GetValue<int>();

        // euler rotation order is format-dependent: bedrock = ZYX, others (free/java) = XYZ
        if (root["meta"]?["model_format"] is JsonNode mf)
            parts.EulerXYZ = mf.GetValue<string>() != "bedrock";

        if (root["textures"] is JsonArray texArr)
            foreach (var t in texArr)
            {
                var tm = new TextureMeta();
                if (t!["name"] is JsonNode nm)             tm.Name = nm.GetValue<string>();
                if (t["uv_width"] is JsonNode uw)          tm.UvWidth = uw.GetValue<int>();
                if (t["uv_height"] is JsonNode uh)         tm.UvHeight = uh.GetValue<int>();
                if (t["frame_time"] is JsonNode ft)        tm.FrameTime = ft.GetValue<float>();
                if (t["frame_order_type"] is JsonNode fo)  tm.FlipType = fo.GetValue<string>();
                parts.Textures.Add(tm);
            }

        foreach (var e in root["elements"]!.AsArray())
            parts.Elements.Add(LoadElement(e!.AsObject()));

        // groups[] = group transforms (uuid/origin/rotation); the outliner holds the hierarchy. Joined by uuid.
        if (root["groups"] is JsonArray groupArr)
            foreach (var g in groupArr)
            {
                var grp = new Group { Uuid = g!["uuid"]!.GetValue<string>() };
                if (g["origin"] is JsonArray o)   grp.Origin   = ToVec3(o);
                if (g["rotation"] is JsonArray r) grp.Rotation = ToVec3(r);
                parts.Groups.Add(grp);
            }

        foreach (var node in root["outliner"]!.AsArray())
        {
            if (node is JsonValue jv && jv.TryGetValue<string>(out var leafUuid))
                parts.Outliner.Add(new OutlinerNode { Uuid = leafUuid, IsGroup = false });
            else
                parts.Outliner.Add(LoadOutlinerNode(node!.AsObject()));
        }

        if (root["animations"] is JsonArray animArr)
            foreach (var a in animArr)
                parts.Animations.Add(LoadAnimation(a!.AsObject()));

        return parts;
    }

    static Element LoadElement(JsonObject obj)
    {
        var el = new Element { Uuid = obj["uuid"]!.GetValue<string>() };
        string type = obj["type"]!.GetValue<string>();

        el.Origin = ToVec3(obj["origin"]!.AsArray());
        if (obj["rotation"] is JsonArray rot) el.Rotation = ToVec3(rot);

        if (type == "cube")
        {
            el.Type = ElementType.Cube;
            el.From = ToVec3(obj["from"]!.AsArray());
            el.To   = ToVec3(obj["to"]!.AsArray());
            var faces = obj["faces"]!.AsObject();
            if (faces["north"] is JsonObject fn) el.North = LoadCubeFace(fn);
            if (faces["south"] is JsonObject fs) el.South = LoadCubeFace(fs);
            if (faces["east"]  is JsonObject fe) el.East  = LoadCubeFace(fe);
            if (faces["west"]  is JsonObject fw) el.West  = LoadCubeFace(fw);
            if (faces["up"]    is JsonObject fu) el.Up    = LoadCubeFace(fu);
            if (faces["down"]  is JsonObject fd) el.Down  = LoadCubeFace(fd);
        }
        else if (type == "mesh")
        {
            el.Type = ElementType.Mesh;
            foreach (var (key, val) in obj["vertices"]!.AsObject())
                el.Vertices[key] = ToVec3(val!.AsArray());
            foreach (var (key, val) in obj["faces"]!.AsObject())
                el.Faces[key] = LoadMeshFace(val!.AsObject());
        }
        else if (type == "locator" || type == "null_object")
        {
            el.Type = type == "locator" ? ElementType.Locator : ElementType.NullObject;
            el.Position = ToVec3(obj["position"]!.AsArray());
        }
        else el.Type = ElementType.Unknown;
        return el;
    }

    static CubeFace LoadCubeFace(JsonObject f)
    {
        var face = new CubeFace();
        var uv = f["uv"]!.AsArray();
        face.U0 = uv[0]!.GetValue<float>();
        face.V0 = uv[1]!.GetValue<float>();
        face.U1 = uv[2]!.GetValue<float>();
        face.V1 = uv[3]!.GetValue<float>();
        if (f["texture"]  is JsonValue tv && tv.TryGetValue<int>(out var ti)) face.Texture = ti;
        if (f["rotation"] is JsonValue rv && rv.TryGetValue<int>(out var ri)) face.Rotation = ri;
        face.Present = true;
        return face;
    }

    static MeshFace LoadMeshFace(JsonObject f)
    {
        var face = new MeshFace();
        foreach (var v in f["vertices"]!.AsArray())
            face.Vertices.Add(v!.GetValue<string>());
        foreach (var (key, val) in f["uv"]!.AsObject())
            face.Uv[key] = ToVec2(val!.AsArray());
        if (f["texture"] is JsonValue tv && tv.TryGetValue<int>(out var ti)) face.Texture = ti;
        return face;
    }

    static OutlinerNode LoadOutlinerNode(JsonObject node)
    {
        var result = new OutlinerNode { Uuid = node["uuid"]!.GetValue<string>(), IsGroup = true };
        foreach (var child in node["children"]!.AsArray())
        {
            if (child is JsonValue jv && jv.TryGetValue<string>(out var s))
                result.Children.Add(new OutlinerNode { Uuid = s, IsGroup = false });
            else
                result.Children.Add(LoadOutlinerNode(child!.AsObject()));
        }
        return result;
    }

    static Animation LoadAnimation(JsonObject obj)
    {
        var anim = new Animation();
        if (obj["name"] is JsonNode nm) anim.Name = nm.GetValue<string>();

        if (obj["loop"] is JsonValue lv)
        {
            if (lv.TryGetValue<string>(out var l))
                anim.Loop = l == "loop" ? LoopMode.Loop
                          : (l == "hold" || l == "hold_on_last_frame") ? LoopMode.Hold
                          : LoopMode.Once;
            else if (lv.TryGetValue<bool>(out var b))
                anim.Loop = b ? LoopMode.Loop : LoopMode.Once;
        }

        if (obj["length"] is JsonNode len) anim.Length = len.GetValue<float>();
        else if (obj["animation_length"] is JsonNode al) anim.Length = al.GetValue<float>();

        if (obj["animators"] is JsonObject animators)
            foreach (var (uuid, val) in animators)
                anim.Animators.Add(LoadBoneAnimator(uuid, val!.AsObject()));
        return anim;
    }

    static BoneAnimator LoadBoneAnimator(string uuid, JsonObject obj)
    {
        var ba = new BoneAnimator { BoneUuid = uuid };
        if (obj["keyframes"] is JsonArray kfs)
            foreach (var k in kfs)
            {
                var kf = k!.AsObject();
                var frame = new KeyFrame { Time = kf["time"]!.GetValue<float>() };

                string interp = kf["interpolation"]!.GetValue<string>();
                frame.Interp = (interp == "catmullrom" || interp == "smooth") ? Interp.Catmullrom
                             : interp == "step"   ? Interp.Step
                             : interp == "bezier" ? Interp.Bezier
                             : Interp.Linear;

                var dps = kf["data_points"]!.AsArray();
                if (dps.Count > 0)
                {
                    var dp = dps[0]!.AsObject();
                    if (dp["x"] is JsonNode dx) frame.X = MolangFrom(dx);
                    if (dp["y"] is JsonNode dy) frame.Y = MolangFrom(dy);
                    if (dp["z"] is JsonNode dz) frame.Z = MolangFrom(dz);
                }

                string channel = kf["channel"]!.GetValue<string>();
                if      (channel == "rotation") ba.Rotation.Add(frame);
                else if (channel == "position") ba.Position.Add(frame);
                else if (channel == "scale")    ba.Scale.Add(frame);
            }

        // Blockbench stores keyframes in CREATION order, not time order; the sampler needs ascending time.
        ba.Rotation.Sort((a, b) => a.Time.CompareTo(b.Time));
        ba.Position.Sort((a, b) => a.Time.CompareTo(b.Time));
        ba.Scale.Sort((a, b) => a.Time.CompareTo(b.Time));
        return ba;
    }

    static MolangExpr MolangFrom(JsonNode? v)
    {
        if (v is JsonValue jv)
        {
            if (jv.TryGetValue<string>(out var s)) return MolangExpr.Compile(s);
            if (jv.TryGetValue<double>(out var d)) return MolangExpr.Compile(d.ToString(CultureInfo.InvariantCulture));
        }
        return MolangExpr.Compile("0");
    }

    static Vector3 ToVec3(JsonArray a) => new(
        a.Count > 0 ? a[0]!.GetValue<float>() : 0,
        a.Count > 1 ? a[1]!.GetValue<float>() : 0,
        a.Count > 2 ? a[2]!.GetValue<float>() : 0);

    static Vector2 ToVec2(JsonArray a) => new(
        a.Count > 0 ? a[0]!.GetValue<float>() : 0,
        a.Count > 1 ? a[1]!.GetValue<float>() : 0);
}
