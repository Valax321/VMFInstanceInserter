using System.Text.RegularExpressions;

namespace VMFInstanceInserter;

class VMFFile
{
    private static Dictionary<string, VMFFile> stVMFCache = new Dictionary<string, VMFFile>();

    public string OriginalPath { get; private set; }
    public VMFStructure Root { get; private set; }
    public VMFStructure World { get; private set; }

    public int LastID { get; private set; }
    public int LastNodeID { get; private set; }

    public VMFFile(string path, string rootDir = null)
    {
        Console.WriteLine("Parsing " + path + "...");

        OriginalPath = (rootDir != null ? rootDir + Path.DirectorySeparatorChar : "") + path;

        if (File.Exists(OriginalPath + ".vmf")) { OriginalPath += ".vmf"; };

        if (!File.Exists(OriginalPath))
        {
            if (rootDir != null && path.Contains('/') && path.Substring(0, path.IndexOf('/')) == rootDir.Substring(rootDir.LastIndexOf('\\') + 1))
                OriginalPath = rootDir + Path.DirectorySeparatorChar + path.Substring(path.IndexOf('/') + 1);

            if (File.Exists(OriginalPath + ".vmf")) { OriginalPath += ".vmf"; };

            if (!File.Exists(OriginalPath))
            {
                Console.WriteLine("File \"" + path + "\" not found!");
                return;
            }
        }

#if !DEBUG
        try {
#endif
        using (var stream = new FileStream(OriginalPath, FileMode.Open, FileAccess.Read))
            Root = new VMFStructure("file", new StreamReader(stream));
#if !DEBUG
        }
        catch( Exception e )
        {
            Console.WriteLine( "Error while parsing file!" );
            Console.WriteLine( e.ToString() );
            return;
        }
#endif

        foreach (var stru in Root)
        {
            if (stru.Type == VMFStructureType.World)
            {
                World = stru;
                break;
            }
        }

        LastID = Root.GetLastID();
        LastNodeID = Root.GetLastNodeID();

        stVMFCache.Add(path, this);
    }

    public void ResolveInstances()
    {
        Console.WriteLine("Resolving instances for " + OriginalPath + "...");
        var structures = Root.Structures;

        var autoName = 0;

        // MAIN ITERATION
        for (var i = structures.Count - 1; i >= 0; --i)
        {
            var structure = structures[i];

            if (structure.Type == VMFStructureType.Entity)
            {
                var classnameVal = structure["classname"];

                if (classnameVal != null) switch (classnameVal.String)
                    {
                        case "func_instance":
                            structures.RemoveAt(i);

                            var fileVal = structure["file"] as VMFStringValue;
                            var originVal = (structure["origin"] as VMFVector3Value) ?? new VMFVector3Value { X = 0, Y = 0, Z = 0 };
                            var anglesVal = (structure["angles"] as VMFVector3Value) ?? new VMFVector3Value { Pitch = 0, Roll = 0, Yaw = 0 };
                            var fixup_styleVal = (structure["fixup_style"] as VMFNumberValue) ?? new VMFNumberValue { Value = 0 };
                            var targetnameVal = structure["targetname"];

                            var pattern = new Regex("^replace[0-9]*$");
                            var replacements = new List<KeyValuePair<string, string>>();
                            var matReplacements = new List<KeyValuePair<string, string>>();

                            foreach (var keyVal in structure.Properties)
                            {
                                if (pattern.IsMatch(keyVal.Key))
                                {
                                    var split = keyVal.Value.String.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                                    if (split.Length < 1)
                                        continue;

                                    if (split[0].StartsWith('#'))
                                    {
                                        matReplacements.Add(new KeyValuePair<string, string>(split[0].Substring(1).Trim(), keyVal.Value.String.Substring(split[0].Length + 1).Trim()));
                                        continue;
                                    }

                                    if (!split[0].StartsWith('$'))
                                    {
                                        Console.WriteLine("Invalid property replacement name \"{0}\" - needs to begin with a $", split[0]);
                                        continue;
                                    }

                                    replacements.Add(new KeyValuePair<string, string>(split[0].Trim(), keyVal.Value.String.Substring(split[0].Length + 1).Trim()));
                                }
                            }

                            replacements = replacements.OrderByDescending(x => x.Key.Length).ToList();
                            matReplacements = matReplacements.OrderByDescending(x => x.Key.Length).ToList();

                            var fixupStyle = (TargetNameFixupStyle)fixup_styleVal.Value;
                            var targetName = (targetnameVal != null ? targetnameVal.String : null);

                            if (fixupStyle != TargetNameFixupStyle.None && targetName == null)
                                targetName = "AutoInstance" + (autoName++);

                            if (fileVal == null)
                            {
                                Console.WriteLine("Invalid instance at (" + originVal.String + ")");
                                continue;
                            }

                            Console.WriteLine("Inserting instance of {0} at ({1}), ({2})", fileVal.String, originVal.String, anglesVal.String);

                            var file = fileVal.String;
                            VMFFile vmf = null;

                            if (stVMFCache.ContainsKey(file))
                                vmf = stVMFCache[file];
                            else
                            {
                                vmf = new VMFFile(file, Path.GetDirectoryName(OriginalPath));
                                if (vmf.Root != null)
                                    vmf.ResolveInstances();
                            }

                            if (vmf.Root == null)
                            {
                                Console.WriteLine("Could not insert!");
                                continue;
                            }

                            foreach (var worldStruct in vmf.World)
                            {
                                if (worldStruct.Type == VMFStructureType.Group || worldStruct.Type == VMFStructureType.Solid)
                                {
                                    var clone = worldStruct.Clone(LastID, LastNodeID, fixupStyle, targetName, replacements, matReplacements);
                                    clone.Transform(originVal, anglesVal);
                                    World.Structures.Add(clone);
                                }
                            }

                            var index = i;

                            foreach (var rootStruct in vmf.Root)
                            {
                                if (rootStruct.Type == VMFStructureType.Entity)
                                {
                                    var clone = rootStruct.Clone(LastID, LastNodeID, fixupStyle, targetName, replacements, matReplacements);
                                    clone.Transform(originVal, anglesVal);
                                    Root.Structures.Insert(index++, clone);
                                }
                            }

                            LastID = Root.GetLastID();
                            LastNodeID = Root.GetLastNodeID();
                            break;
                        case "func_instance_parms":
                            structures.RemoveAt(i);
                            break;
                    }
            }
        }

        Console.WriteLine("Instances resolved.");
    }

    public void Save(string path)
    {
        Console.WriteLine("Saving to " + path + "...");

        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            Root.Write(new StreamWriter(stream));
    }
}
