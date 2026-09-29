using System;
using System.Collections;
using System.IO;
using System.Resources;

// Minimal resgen: converts a .resx to a .NET Framework .resources file (args: in.resx out.resources)
class ResGen {
	static int Main(string[] args) {
		using (var reader = new ResXResourceReader(args[0]))
		using (var writer = new ResourceWriter(args[1])) {
			reader.BasePath = Path.GetDirectoryName(Path.GetFullPath(args[0]));
			int n = 0;
			foreach (DictionaryEntry entry in reader) {
				writer.AddResource((string)entry.Key, entry.Value);
				n++;
			}
			writer.Generate();
			Console.WriteLine(Path.GetFileName(args[0]) + ": " + n + " resources");
		}
		return 0;
	}
}
