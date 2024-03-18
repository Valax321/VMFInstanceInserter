using System.Text.RegularExpressions;

namespace VMFInstanceInserter;

enum VMFStructureType
{
    File,
    VersionInfo,
    VisGroups,
    ViewSettings,
    World,
    Solid,
    Side,
    Editor,
    Entity,
    Connections,
    Group,
    Cameras,
    Camera,
    Cordon,
    Visgroup,
    DispInfo,
    Hidden,
    Normals,
    Distances,
    Offsets,
    Offset_Normals,
    Alphas,
    Triangle_Tags,
    Allowed_Verts,
    Unknown
}

enum TransformType
{
    None = 0,
    Offset = 1,
    Angle = 2,
    Position = 3,
    EntityName = 4,
    Identifier = 5
}

enum TargetNameFixupStyle
{
    Prefix = 0,
    Postfix = 1,
    None = 2
}

class VMFStructure : IEnumerable<VMFStructure>
{
    private static readonly Dictionary<string, VMFStructureType> stTypeDict;

    private static readonly Dictionary<VMFStructureType, Dictionary<string, TransformType>> stTransformDict = new Dictionary<VMFStructureType, Dictionary<string, TransformType>>
    {
        { VMFStructureType.Side, new Dictionary<string, TransformType>
        {
            { "plane", TransformType.Position },
            { "uaxis", TransformType.Position },
            { "vaxis", TransformType.Position }
        } },
        { VMFStructureType.Entity, new Dictionary<string, TransformType>
        {
            { "origin", TransformType.Position },
            { "angles", TransformType.Angle },
            { "targetname", TransformType.EntityName },
            { "parentname", TransformType.EntityName },
            { "lowerleft", TransformType.Position },
            { "lowerright", TransformType.Position },
            { "upperleft", TransformType.Position },
            { "upperright", TransformType.Position }
        } },
        { VMFStructureType.DispInfo, new Dictionary<string, TransformType>
        {
            { "startposition", TransformType.Position }
        } },
        { VMFStructureType.Normals, new Dictionary<string, TransformType>
        {
            { "row[0-9]+", TransformType.Offset }
        } },
        { VMFStructureType.Offsets, new Dictionary<string, TransformType>
        {
            { "row[0-9]+", TransformType.Offset }
        } },
        { VMFStructureType.Offset_Normals, new Dictionary<string, TransformType>
        {
            { "row[0-9]+", TransformType.Offset }
        } }
    };

    private static readonly Dictionary<string, Dictionary<string, TransformType>> stEntitiesDict = new Dictionary<string, Dictionary<string, TransformType>>();
    private static readonly Dictionary<string, TransformType> stInputsDict = new Dictionary<string, TransformType>();

    static VMFStructure()
    {
        stTypeDict = new Dictionary<string, VMFStructureType>();

        foreach (var name in Enum.GetNames(typeof(VMFStructureType)))
            stTypeDict.Add(name.ToLower(), (VMFStructureType)Enum.Parse(typeof(VMFStructureType), name));
    }

    private static string TrimFGDLine(string line)
    {
        line = line.Trim();

        var escaped = false;
        var inString = false;
        for (var i = 0; i < line.Length; ++i)
        {
            var c = line[i];
            if (escaped)
            {
                escaped = false; break;
            }

            if (c == '\\')
            {
                escaped = true;
            }
            else if (c == '"')
            {
                inString = !inString;
            }
            else if (!inString && c == '/')
            {
                if (i < line.Length - 1 && line[i + 1] == '/')
                {
                    return line.Substring(0, i).TrimEnd();
                }
            }
        }

        return line;
    }

    private static readonly Regex _sIncludeRegex = new Regex("^@include \"[^\"]+\"");
    private static readonly Regex _sClassTypeRegex = new Regex("^@(?<classType>[A-Z]([A-Za-z])*Class)( |=)");
    private static readonly Regex _sBaseDefRegex = new Regex("base\\(\\s*[A-Za-z0-9_]+(\\s*,\\s*[A-Za-z0-9_]+)*\\s*\\)");
    private static readonly Regex _sParamDefRegex = new Regex("^[a-zA-Z0-9_]+\\s*\\(\\s*[A-Za-z0-9_]+\\s*\\)(\\s*readonly\\s*|\\s*):.*$");
    public static void ParseFGD(string path)
    {
        Console.WriteLine("Loading {0}", Path.GetFileName(path));

        if (!File.Exists(path))
        {
            Console.WriteLine("File does not exist!");
            return;
        }

        var reader = new StreamReader(path);

        string curName = null;
        Dictionary<string, TransformType> curDict = null;

        while (!reader.EndOfStream)
        {
            var line = TrimFGDLine(reader.ReadLine());
            if (line.Length == 0) continue;
            while ((line.EndsWith("+") || line.EndsWith(":")) && !reader.EndOfStream)
            {
                line = line.TrimEnd('+', ' ', '\t') + TrimFGDLine(reader.ReadLine());
            }

            Match match;

            if (_sIncludeRegex.IsMatch(line))
            {
                var start = line.IndexOf('"') + 1;
                var end = line.IndexOf('"', start);
                ParseFGD(Path.Combine(Path.GetDirectoryName(path), line.Substring(start, end - start)));
            }
            else if ((match = _sClassTypeRegex.Match(line)).Success)
            {
                var start = line.IndexOf('=') + 1;
                var end = Math.Max(line.IndexOf(':', start), line.IndexOf('[', start));
                if (end == -1) end = line.Length;
                curName = line.Substring(start, end - start).Trim();

                if (!stEntitiesDict.ContainsKey(curName))
                {
                    stEntitiesDict.Add(curName, new Dictionary<string, TransformType>());
                }

                curDict = stEntitiesDict[curName];

                // Don't rotate angles for brush entities
                if (match.Groups["classType"].Value.Equals("SolidClass", StringComparison.InvariantCultureIgnoreCase))
                {
                    curDict.Add("angles", TransformType.None);
                }

                var basesMatch = _sBaseDefRegex.Match(line);
                while (basesMatch.Success && basesMatch.Index < start)
                {
                    var baseStart = basesMatch.Value.IndexOf('(') + 1;
                    var baseEnd = basesMatch.Value.IndexOf(')', baseStart);
                    var bases = basesMatch.Value.Substring(baseStart, baseEnd - baseStart).Split(',');

                    foreach (var baseName in bases)
                    {
                        var trimmed = baseName.Trim();
                        if (stEntitiesDict.ContainsKey(trimmed))
                        {
                            foreach (var keyVal in stEntitiesDict[trimmed])
                            {
                                if (!curDict.ContainsKey(keyVal.Key))
                                {
                                    curDict.Add(keyVal.Key, keyVal.Value);
                                }
                            }
                        }
                        else
                        {
                            Console.WriteLine("Undefined parent for class {0} : {1}", curName, trimmed);
                        }
                    }

                    basesMatch = basesMatch.NextMatch();
                }
            }
            else if (curDict != null && _sParamDefRegex.IsMatch(line))
            {
                var start = line.IndexOf('(') + 1;
                var end = line.IndexOf(')', start);
                var name = line.Substring(0, start - 1).TrimEnd();
                var typeName = line.Substring(start, end - start).Trim().ToLower();

                var type = TransformType.None;
                switch (typeName)
                {
                    case "angle":
                        type = TransformType.Angle;
                        break;
                    case "origin":
                        type = TransformType.Position;
                        break;
                    case "target_destination":
                    case "target_source":
                    case "filterclass":
                        type = TransformType.EntityName;
                        break;
                    case "vecline":
                        // Single point axis helpers (see phys_motor, for example) are stored
                        // as absolute world coordinates, not angles as one might expect.
                        type = TransformType.Position;
                        break;
                    case "vector":
                        // Temporary hack to fix mistake on valve's part
                        if (curName == "func_useableladder" && (name == "point0" || name == "point1"))
                        {
                            type = TransformType.Position;
                        }
                        else if (curName == "info_overlay")
                        {
                            if (name == "BasisOrigin")
                                type = TransformType.Position;
                            else if (name == "BasisNormal" || name == "BasisU" || name == "BasisV")
                                type = TransformType.Offset;
                            else
                                type = TransformType.None;
                        }
                        else
                        {
                            type = TransformType.Offset;
                        }
                        break;
                    case "sidelist":
                        type = TransformType.Identifier;
                        break;
                }

                if (!curDict.ContainsKey(name))
                {
                    curDict.Add(name, type);
                }
                else
                {
                    curDict[name] = type;
                }
            }
        }
    }

    private static TransformType ParseTransformType(string str)
    {
        switch (str.ToLower())
        {
            case "n":
            case "null":
            case "nil":
            case "none":
                return TransformType.None;
            case "o":
            case "off":
            case "offset":
                return TransformType.Offset;
            case "a":
            case "ang":
            case "angle":
                return TransformType.Angle;
            case "p":
            case "pos":
            case "position":
                return TransformType.Position;
            case "e":
            case "ent":
            case "entity":
                return TransformType.EntityName;
            case "i":
            case "id":
            case "ident":
            case "identifier":
                return TransformType.Identifier;
            default:
                Console.WriteLine("Bad transform type: " + str);
                return TransformType.None;
        }
    }

    private static string FixupName(string name, TargetNameFixupStyle fixupStyle, string targetName)
    {
        if (fixupStyle == TargetNameFixupStyle.None || targetName == null || name.StartsWith("@") || name.StartsWith("!"))
            return name;

        switch (fixupStyle)
        {
            case TargetNameFixupStyle.Postfix:
                return name + targetName;
            case TargetNameFixupStyle.Prefix:
                return targetName + name;
            default:
                return name;
        }
    }

    private int myIDIndex;

    public VMFStructureType Type { get; private set; }
    public int ID
    {
        get
        {
            if (myIDIndex == -1)
                return 0;

            return (int)(Properties[myIDIndex].Value as VMFNumberValue).Value;
        }
        set
        {
            if (myIDIndex == -1)
                return;

            (Properties[myIDIndex].Value as VMFNumberValue).Value = value;
        }
    }

    public List<KeyValuePair<string, VMFValue>> Properties { get; private set; }
    public List<VMFStructure> Structures { get; private set; }

    public VMFValue this[string key]
    {
        get
        {
            foreach (var keyVal in Properties)
                if (keyVal.Key == key)
                    return keyVal.Value;

            return null;
        }
    }

    private static readonly Dictionary<string, TransformType> stDefaultEntDict = new Dictionary<string, TransformType>();
    private VMFStructure(VMFStructure clone, int idOffset, int nodeOffset, TargetNameFixupStyle fixupStyle, string targetName,
        List<KeyValuePair<string, string>> replacements, List<KeyValuePair<string, string>> matReplacements)
    {
        Type = clone.Type;

        Properties = new List<KeyValuePair<string, VMFValue>>();
        Structures = new List<VMFStructure>();

        myIDIndex = clone.myIDIndex;

        var entDict = stDefaultEntDict;

        if (Type == VMFStructureType.Entity)
        {
            var className = clone["classname"].String;
            if (className != null && stEntitiesDict.ContainsKey(className))
                entDict = stEntitiesDict[className];
        }

        foreach (var keyVal in clone.Properties)
        {
            var str = keyVal.Value.String;
            var fixup = true;
            if (replacements != null && str.Contains("$"))
            {
                fixup = false;
                foreach (var repKeyVal in replacements)
                    str = str.Replace(repKeyVal.Key, repKeyVal.Value);
            }

            KeyValuePair<string, VMFValue> kvClone;

            if (keyVal.Value is VMFVector3ArrayValue)
            {
                kvClone = new KeyValuePair<string, VMFValue>(keyVal.Key, new VMFVector3ArrayValue() { String = str });
            }
            else
            {
                kvClone = new KeyValuePair<string, VMFValue>(keyVal.Key, VMFValue.Parse(str));
            }

            if (Type == VMFStructureType.Connections)
            {
                if (fixup && fixupStyle != TargetNameFixupStyle.None && targetName != null)
                {
                    var split = kvClone.Value.String.Split(',');
                    split[0] = FixupName(split[0], fixupStyle, targetName);
                    if (stInputsDict.ContainsKey(split[1]))
                    {
                        switch (stInputsDict[split[1]])
                        {
                            case TransformType.EntityName:
                                split[2] = FixupName(split[2], fixupStyle, targetName);
                                break;
                                // add more later
                        }
                    }
                    kvClone.Value.String = string.Join(",", split);
                }
            }
            else
            {
                if (Type == VMFStructureType.Side && matReplacements != null && kvClone.Key == "material")
                {
                    var material = kvClone.Value.String;
                    foreach (var repKeyVal in matReplacements)
                    {
                        if (material == repKeyVal.Key)
                        {
                            ((VMFStringValue)kvClone.Value).String = repKeyVal.Value;
                            break;
                        }
                    }
                }
                else if (kvClone.Key == "groupid")
                {
                    ((VMFNumberValue)kvClone.Value).Value += idOffset;
                }
                else if (kvClone.Key == "nodeid")
                {
                    ((VMFNumberValue)kvClone.Value).Value += nodeOffset;
                }
                else if (Type == VMFStructureType.Entity)
                {
                    var trans = entDict.ContainsKey(kvClone.Key) ? entDict[kvClone.Key] : TransformType.None;

                    if (trans == TransformType.Identifier)
                    {
                        kvClone.Value.OffsetIdentifiers(idOffset);
                    }
                    else if (fixup && (kvClone.Key == "targetname" || trans == TransformType.EntityName) && fixupStyle != TargetNameFixupStyle.None && targetName != null)
                    {
                        kvClone = new KeyValuePair<string, VMFValue>(kvClone.Key, new VMFStringValue { String = FixupName(kvClone.Value.String, fixupStyle, targetName) });
                    }
                }
            }

            Properties.Add(kvClone);
        }

        foreach (var structure in clone.Structures)
            Structures.Add(new VMFStructure(structure, idOffset, nodeOffset, fixupStyle, targetName, replacements, matReplacements));

        ID += idOffset;
    }

    public VMFStructure(string type, StreamReader reader)
    {
        if (stTypeDict.ContainsKey(type))
            Type = stTypeDict[type];
        else
            Type = VMFStructureType.Unknown;

        Properties = new List<KeyValuePair<string, VMFValue>>();
        Structures = new List<VMFStructure>();

        myIDIndex = -1;

        string line;
        while (!reader.EndOfStream && (line = reader.ReadLine().Trim()) != "}")
        {
            if (line == "{" || line.Length == 0)
                continue;

            if (line[0] == '"')
            {
                var pair = line.Trim('"').Split(new string[] { "\" \"" }, StringSplitOptions.None);
                if (pair.Length != 2)
                    continue;

                KeyValuePair<string, VMFValue> keyVal;

                if (Type == VMFStructureType.Normals || Type == VMFStructureType.Offsets || Type == VMFStructureType.Offset_Normals)
                {
                    keyVal = new KeyValuePair<string, VMFValue>(pair[0], new VMFVector3ArrayValue() { String = pair[1] });
                }
                else
                {
                    keyVal = new KeyValuePair<string, VMFValue>(pair[0], VMFValue.Parse(pair[1]));
                }

                if (keyVal.Key == "id" && keyVal.Value is VMFNumberValue)
                    myIDIndex = Properties.Count;

                Properties.Add(keyVal);
            }
            else
            {
                Structures.Add(new VMFStructure(line, reader));
            }
        }
    }

    public void Write(StreamWriter writer, int depth = 0)
    {
        if (Type == VMFStructureType.File)
        {
            foreach (var structure in Structures)
                structure.Write(writer, depth);
        }
        else
        {
            var indent = "";
            for (var i = 0; i < depth; ++i)
                indent += "\t";

            writer.WriteLine(indent + Type.ToString().ToLower());
            writer.WriteLine(indent + "{");
            foreach (var keyVal in Properties)
                writer.WriteLine(indent + "\t\"" + keyVal.Key + "\" \"" + keyVal.Value.String + "\"");
            foreach (var structure in Structures)
                structure.Write(writer, depth + 1);
            writer.WriteLine(indent + "}");
        }

        writer.Flush();
    }

    public VMFStructure Clone(int idOffset = 0, int nodeOffset = 0, TargetNameFixupStyle fixupStyle = TargetNameFixupStyle.None,
        string targetName = null, List<KeyValuePair<string, string>> replacements = null, List<KeyValuePair<string, string>> matReplacements = null)
    {
        return new VMFStructure(this, idOffset, nodeOffset, fixupStyle, targetName, replacements, matReplacements);
    }

    public void Transform(VMFVector3Value translation, VMFVector3Value rotation)
    {
        Dictionary<string, TransformType> transDict = null;
        Dictionary<string, TransformType> entDict = null;

        if (stTransformDict.ContainsKey(Type))
            transDict = stTransformDict[Type];

        if (Type == VMFStructureType.Entity)
        {
            var className = this["classname"].String;
            if (className != null && stEntitiesDict.ContainsKey(className))
                entDict = stEntitiesDict[className];
        }

        if (transDict != null || entDict != null)
        {
            foreach (var keyVal in Properties)
            {
                var trans = TransformType.None;

                if (transDict != null)
                {
                    foreach (var key in transDict.Keys)
                    {
                        if (Regex.IsMatch(keyVal.Key, key))
                        {
                            trans = transDict[key];
                        }
                    }
                }

                if (entDict != null && entDict.ContainsKey(keyVal.Key))
                    trans = entDict[keyVal.Key];

                switch (trans)
                {
                    case TransformType.Offset:
                        keyVal.Value.Rotate(rotation);
                        break;
                    case TransformType.Angle:
                        keyVal.Value.AddAngles(rotation);
                        break;
                    case TransformType.Position:
                        keyVal.Value.Rotate(rotation);
                        keyVal.Value.Offset(translation);
                        break;
                }
            }
        }

        foreach (var structure in Structures)
            structure.Transform(translation, rotation);
    }

    public int GetLastID()
    {
        var max = ID;

        foreach (var structure in Structures)
            max = Math.Max(structure.GetLastID(), max);

        return max;
    }

    public int GetLastNodeID()
    {
        var max = ContainsKey("nodeid") ? (int)((VMFNumberValue)this["nodeid"]).Value : 0;

        foreach (var structure in Structures)
            max = Math.Max(structure.GetLastNodeID(), max);

        return max;
    }

    public bool ContainsKey(string key)
    {
        foreach (var keyVal in Properties)
            if (keyVal.Key == key)
                return true;

        return false;
    }

    public override string ToString()
    {
        return Type + " {}";
    }

    public IEnumerator<VMFStructure> GetEnumerator()
    {
        return Structures.GetEnumerator();
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
    {
        return Structures.GetEnumerator();
    }
}
