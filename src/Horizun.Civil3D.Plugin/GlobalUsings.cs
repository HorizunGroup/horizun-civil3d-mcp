// WPF (ribbon) removes System.IO from the implicit usings because of
// System.Windows.Shapes.Path; restore it explicitly and pin Path to System.IO.
global using System.IO;
global using Path = System.IO.Path;
