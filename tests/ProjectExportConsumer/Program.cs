using FastChartsScene.Examples;
using FastChartsScene.Export;
if(args.Length!=1)throw new ArgumentException("Specify an output zip path.");
using var document=ExampleCatalog.Find("signals").Create();document.Title="Extracted package-only chart";
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0]))!);
using var stream=File.Create(args[0]);ChartProjectExport.Write(document,stream);
