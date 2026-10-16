// Build tool only. This file is NEVER compiled into MicNeverDown.exe.
// Uses the Roslyn compiler shipped with PowerShell 7; no network or packages.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace MicNeverDown.Build
{
    public static class Compiler
    {
        public static readonly string[] ApplicationFiles = new[] {
            "Program.cs", "TrayApp.cs", "SettingsForm.cs", "GuardEngine.cs",
            "ModelsAndStorage.cs", "Audio.cs", "Startup.cs", "Localization.cs", "AssemblyInfo.cs"
        };

        public static string Compile(string sourceDirectory, string outputPath, string[] frameworkReferences)
        {
            var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp7_3,
                DocumentationMode.None, SourceCodeKind.Regular);
            var trees = new List<SyntaxTree>();
            foreach (string name in ApplicationFiles)
            {
                // Stable virtual paths keep local directory names out of the PE.
                SourceText source = SourceText.From(File.ReadAllText(Path.Combine(sourceDirectory, name),
                    Encoding.UTF8), Encoding.UTF8);
                trees.Add(CSharpSyntaxTree.ParseText(source, parseOptions, "src/" + name));
            }

            var references = frameworkReferences.Select(path => MetadataReference.CreateFromFile(path));
            var options = new CSharpCompilationOptions(OutputKind.WindowsApplication,
                optimizationLevel: OptimizationLevel.Release,
                platform: Platform.AnyCpu,
                allowUnsafe: false,
                checkOverflow: false,
                deterministic: true,
                concurrentBuild: false);
            CSharpCompilation compilation = CSharpCompilation.Create("MicNeverDown", trees, references, options);
            string iconPath = Path.Combine(sourceDirectory, "Cat.ico");
            string manifestPath = Path.Combine(sourceDirectory, "app.manifest");
            var resources = new[] {
                new ResourceDescription("MicNeverDown.Cat.ico", () => File.OpenRead(iconPath), true)
            };

            using (var manifest = File.OpenRead(manifestPath))
            using (var icon = File.OpenRead(iconPath))
            using (var win32 = compilation.CreateDefaultWin32Resources(
                versionResource: true, noManifest: false,
                manifestContents: manifest, iconInIcoFormat: icon))
            using (var output = new MemoryStream())
            {
                EmitResult result = compilation.Emit(output, win32Resources: win32,
                    manifestResources: resources, options: new EmitOptions());
                string diagnostics = String.Join(Environment.NewLine,
                    result.Diagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning)
                        .Select(d => d.ToString()));
                if (!result.Success)
                    throw new InvalidOperationException("Application compilation failed:" + Environment.NewLine + diagnostics);

                // A failed compiler run leaves no partial executable behind.
                File.WriteAllBytes(outputPath, output.ToArray());
                return diagnostics;
            }
        }
    }
}
