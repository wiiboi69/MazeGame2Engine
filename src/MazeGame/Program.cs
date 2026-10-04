using MazeGame;

// Optional arguments:  MazeGame [levels-directory]
string baseDir = AppContext.BaseDirectory;
string levels = args.Length > 0 ? args[0] : Path.Combine(baseDir, "levels");
using var game = new Game(Path.Combine(baseDir, "assets"), levels, Path.Combine(baseDir, "settings.json"));
game.Run();
