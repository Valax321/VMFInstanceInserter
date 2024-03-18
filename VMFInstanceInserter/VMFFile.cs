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

    private void ResolveInstanceProxyConnections(VMFStructure connections, Dictionary<string, List<IOProxyConnection>> outMappedOutputConnections)
    {
        foreach (var (targetString, output) in connections.Properties)
        {
            var targetParams = targetString.Split(';');
            var targetName = targetParams[0];
            var targetOutput = targetParams[1];

            if (targetName.StartsWith("instance:"))
            {
                // This is the instance-local name of the entity to write this output to
                targetName = targetName.Replace("instance:", string.Empty);
                var list = outMappedOutputConnections.GetOrAdd(targetName, (_) => []);
                Console.WriteLine($"Found proxy output: {targetString}");
                list.Add(new IOProxyConnection()
                {
                    OutputName = targetOutput,
                    InvokeString = output.String
                });
            }
        }
    }

    public void ResolveInstances(bool removeIOProxies = true)
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

                            var mappedOutputsForInstance = new Dictionary<string, List<IOProxyConnection>>();
                            var mappedInputsForInstance = new Dictionary<string, List<IOProxyConnection>>();

                            var fileVal = structure["file"] as VMFStringValue;
                            var originVal = (structure["origin"] as VMFVector3Value) ?? new VMFVector3Value { X = 0, Y = 0, Z = 0 };
                            var anglesVal = (structure["angles"] as VMFVector3Value) ?? new VMFVector3Value { Pitch = 0, Roll = 0, Yaw = 0 };
                            var fixup_styleVal = (structure["fixup_style"] as VMFNumberValue) ?? new VMFNumberValue { Value = 0 };
                            var targetnameVal = structure["targetname"];

                            var pattern = new Regex("^replace[0-9]*$");
                            var replacements = new List<KeyValuePair<string, string>>();
                            var matReplacements = new List<KeyValuePair<string, string>>();

                            var connections = structure.Structures.FirstOrDefault(x => x.Type == VMFStructureType.Connections);
                            if (connections != null)
                            {
                                ResolveInstanceProxyConnections(connections, mappedOutputsForInstance);
                            }

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

                            var needsResolution = false;
                            if (stVMFCache.ContainsKey(file))
                                vmf = stVMFCache[file];
                            else
                            {
                                vmf = new VMFFile(file, Program.GameDir); // EVIL HACK
                                needsResolution = true;
                            }

                            if (vmf.Root == null)
                            {
                                Console.WriteLine("Could not insert!");
                                continue;
                            }

                            if (needsResolution)
                            {
                                if (vmf.Root != null)
                                    vmf.ResolveInstances();
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

                                    // Add each found proxied output
                                    var rsTargetname = rootStruct["targetname"];
                                    if (rsTargetname != null && mappedOutputsForInstance.TryGetValue(rsTargetname.String, out var rsConnections))
                                    {
                                        var rsConnectionsStruct = clone.FirstOrDefault(x => x.Type == VMFStructureType.Connections);
                                        if (rsConnectionsStruct == null)
                                        {
                                            rsConnectionsStruct = new VMFStructure(VMFStructureType.Connections);
                                            clone.Structures.Add(rsConnectionsStruct);
                                        }

                                        foreach (var rsConnection in rsConnections)
                                        {
                                            Console.WriteLine($"Remapped proxy output {rsConnection.OutputName} to {clone["targetname"]?.String}");

                                            rsConnectionsStruct.Properties.Add(new KeyValuePair<string, VMFValue>(
                                                rsConnection.OutputName,
                                                new VMFStringValue() { String = rsConnection.InvokeString }
                                            ));
                                        }

                                        // Remove all instance ProxyRelay outputs (the target entity won't exist)
                                        rsConnectionsStruct.Properties.RemoveAll(x => x.Value.String.Contains(",ProxyRelay,"));
                                    }
                                }
                            }

                            LastID = Root.GetLastID();
                            LastNodeID = Root.GetLastNodeID();
                            break;
                        case "func_instance_io_proxy":
                            if (removeIOProxies)
                                structures.RemoveAt(i);
                            break;
                        case "func_instance_parms":
                            structures.RemoveAt(i);
                            break;
                        default:
                            var curEntConnections = structure.FirstOrDefault(x => x.Type == VMFStructureType.Connections);
                            if (curEntConnections != null)
                            {
                                var newProps = new List<KeyValuePair<string, (string, VMFValue)>>();

                                foreach (var (targetString, output) in curEntConnections.Properties)
                                {
                                    var outputParts = output.String.Split(',', StringSplitOptions.None);
                                    var instanceName = outputParts[0];
                                    var inputName = outputParts[1];

                                    if (inputName.StartsWith("instance:"))
                                    {
                                        inputName = inputName.Replace("instance:", string.Empty);
                                        var inputNameParts = inputName.Split(';');
                                        inputName = inputNameParts[0];
                                        var inputCommand = inputNameParts[1];

                                        var fixedUpNameHack = instanceName + "-" + inputName;
                                        newProps.Add(new KeyValuePair<string, (string, VMFValue)>(output.String, (targetString, new VMFStringValue() { String = $"{fixedUpNameHack},{inputCommand}," + string.Join(',', outputParts[2..]) })));
                                    }
                                }

                                curEntConnections.Properties.RemoveAll(x => newProps.Any(y => x.Value.String == y.Key));
                                foreach (var (_, connection) in newProps)
                                {
                                    curEntConnections.Properties.Add(new KeyValuePair<string, VMFValue>(connection.Item1, connection.Item2));
                                }
                            }
                            break;
                    }
            }
        }

        Console.WriteLine("Instances resolved.");
    }

    public void Save(string path)
    {
        Console.WriteLine("Saving to " + path + "...");

        Directory.CreateDirectory(Path.GetDirectoryName(path));

        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            Root.Write(new StreamWriter(stream));
    }
}
