using MazeGame;
using MazeGame.Core.Scripting;

// Optional arguments:  MazeGame [levels-directory]
// Script tools:        MazeGame --compile script.mgs script.mgb     compile source to bytecode
//                      MazeGame --disasm script.mgb|script.mgs      print the bytecode
if (args.Length >= 2 && args[0] == "--compile")
{
    var chunk = ScriptCompiler.Compile(File.ReadAllText(args[1]), Path.GetFileNameWithoutExtension(args[1]));
    chunk.Save(args.Length > 2 ? args[2] : Path.ChangeExtension(args[1], ".mgb"));
    Console.WriteLine("compiled " + args[1]);
    return;
}
if (args.Length >= 2 && args[0] == "--disasm")
{
    var chunk = args[1].EndsWith(".mgs", StringComparison.OrdinalIgnoreCase)
        ? ScriptCompiler.Compile(File.ReadAllText(args[1]), Path.GetFileNameWithoutExtension(args[1]))
        : Chunk.Load(args[1]);
    Console.WriteLine(chunk.Disassemble());
    return;
}

MazeGame.Core.GameContent.RegisterAll();   // your tiles and entities (src/MazeGame.Core/Content/GameContent.cs)
string baseDir = AppContext.BaseDirectory;
string levels = args.Length > 0 ? args[0] : Path.Combine(baseDir, "levels");
using var game = new Game(Path.Combine(baseDir, "assets"), levels, Path.Combine(baseDir, "settings.json"));
game.Run();
