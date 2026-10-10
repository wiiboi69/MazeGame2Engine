using MazeGame.Editor;

// Usage: MazeEditor [levels-directory]
// Without an argument the editor edits the "levels" folder next to the executable.
MazeGame.Core.GameContent.RegisterAll();   // your tiles and entities (src/MazeGame.Core/Content/GameContent.cs)
string baseDir = AppContext.BaseDirectory;
string levels = args.Length > 0 ? args[0] : Path.Combine(baseDir, "levels");
using var editor = new EditorApp(Path.Combine(baseDir, "assets"), levels);
editor.Run();
