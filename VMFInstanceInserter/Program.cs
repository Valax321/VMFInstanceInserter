namespace VMFInstanceInserter;

class Program
{
    public static string GameDir = null;

    static void Main(string[] args)
    {
#if !DEBUG
        Directory.SetCurrentDirectory(Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory));
#endif

        var paths = new List<string>();
        var cleanup = false;

        var fgdpaths = Array.Empty<string>();

        for (var i = 0; i < args.Length; ++i)
        {
            var arg = args[i];
            if (!arg.StartsWith("-"))
                paths.Add(arg);
            else
            {
                switch (arg.Substring(1).ToLower())
                {
                    case "c":
                    case "-cleanup":
                        cleanup = true;
                        break;
                    case "d":
                    case "-fgd":
                        fgdpaths = args[++i].Split(',').Select(x => x.Trim()).ToArray();
                        break;
                    case "i":
                    case "-instancedir":
                        GameDir = args[++i];
                            break;
                }
            }
        }

        if (paths.Count < 1)
        {
            Console.WriteLine("Unexpected arguments. Aborting...");
            return;
        }

        var vmf = paths[0];
        var rootName = Path.GetDirectoryName(vmf) + Path.DirectorySeparatorChar + Path.GetFileNameWithoutExtension(vmf);
        var dest = (paths.Count >= 2 ? paths[1] : rootName + ".temp.vmf");
        var del = "Deleting ";
        var renaming = "Renaming {0} to {1}";

        if (cleanup)
        {
            if (File.Exists(dest))
            {
                Console.WriteLine(del + dest);
                File.Delete(dest);
            }

            var prt = rootName + ".prt";
            var tempPrt = rootName + ".temp.prt";

            if (File.Exists(tempPrt))
            {
                if (File.Exists(prt))
                {
                    Console.WriteLine(del + prt);
                    File.Delete(prt);
                }

                Console.WriteLine(renaming, tempPrt, prt);
                File.Move(tempPrt, prt);
            }

            var lin = rootName + ".lin";
            var tempLin = rootName + ".temp.lin";

            if (File.Exists(lin))
            {
                Console.WriteLine(del + lin);
                File.Delete(lin);
            }

            if (File.Exists(tempLin))
            {
                Console.WriteLine(renaming, tempLin, lin);
                File.Move(tempLin, lin);
            }
        }
        else
        {
            foreach (var path in fgdpaths)
            {
                VMFStructure.ParseFGD(path);
            }

            var file = new VMFFile(vmf);
            file.ResolveInstances();
            file.Save(dest);
        }

#if DEBUG
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
#endif
    }
}
