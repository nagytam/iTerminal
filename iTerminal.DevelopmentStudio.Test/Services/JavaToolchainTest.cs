using iTerminal.DevelopmentStudio.Model;
using iTerminal.DevelopmentStudio.Services;

namespace iTerminal.DevelopmentStudio.Test.Services;

[TestClass]
public class JavaToolchainTest
{
    private static string Exe(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;

    /// <summary>A folder with a package tree: <c>app/Main.java</c> and <c>app/Util.java</c> under <c>src</c>.</summary>
    private static string WritePackageTree(string folder)
    {
        string app = Path.Combine(folder, "src", "app");
        Directory.CreateDirectory(app);
        File.WriteAllText(Path.Combine(app, "Main.java"), "package app;\npublic class Main { public static void main(String[] a) { System.out.println(Util.hello()); } }\n");
        File.WriteAllText(Path.Combine(app, "Util.java"), "package app;\nclass Util { static String hello() { return \"hi\"; } }\n");
        return Path.Combine(app, "Main.java");
    }

    [TestMethod]
    public void TheSourceRootIsTheFolderAboveThePackageFolders()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"JavaRoot_{Guid.NewGuid():N}");
        try
        {
            string main = WritePackageTree(folder);

            Assert.AreEqual(Path.Combine(folder, "src"), JavaSourceLayout.RootOf(main));
            Assert.AreEqual("app", JavaSourceLayout.PackageOf(main));
            Assert.AreEqual("app.Main", JavaSourceLayout.QualifiedName(main));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public void AFileOutsideItsPackageFoldersAndTheDefaultPackageUseTheirOwnFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"JavaDefault_{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            string plain = Path.Combine(folder, "Plain.java");
            File.WriteAllText(plain, "public class Plain { }\n");
            string misplaced = Path.Combine(folder, "Misplaced.java");
            File.WriteAllText(misplaced, "package not.where.it.is;\nclass Misplaced { }\n");

            Assert.AreEqual(folder, JavaSourceLayout.RootOf(plain));
            Assert.AreEqual(folder, JavaSourceLayout.RootOf(misplaced), "a file that is not in its package's folders is not moved to another root");
            Assert.AreEqual("Plain", JavaSourceLayout.QualifiedName(plain));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public void ASingleFileBuildsIntoItsSourceRootAndRunsItsCompiledClass()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"JavaBuild_{Guid.NewGuid():N}");
        try
        {
            string main = WritePackageTree(folder);
            var file = new DevelopmentProject("Main.java", DevelopmentLanguage.Java, DevelopmentProjectKind.Script, main);
            var toolchain = new JavaToolchain();
            string classes = Path.Combine(folder, "src", JavaSourceLayout.ClassesFolderName);

            ProcessCommand build = toolchain.Build(file)!;
            CollectionAssert.AreEqual(new[] { "-d", classes, "-sourcepath", Path.Combine(folder, "src"), main }, build.Arguments.ToArray());
            Assert.AreEqual("javac", build.FileName, "the tools on the PATH when no JDK is chosen");
            Assert.IsNull(build.Environment, "no JAVA_HOME is set for the PATH");

            CollectionAssert.AreEqual(new[] { main }, toolchain.Run(file)!.Arguments.ToArray(), "before a build there is no class, so the source launcher runs the file");

            string classFile = Path.Combine(classes, "app", "Main.class");
            Directory.CreateDirectory(Path.GetDirectoryName(classFile)!);
            File.WriteAllText(classFile, string.Empty);
            CollectionAssert.AreEqual(new[] { "-cp", classes, "app.Main" }, toolchain.Run(file)!.Arguments.ToArray());

            CollectionAssert.AreEqual(new[] { classes }, toolchain.CacheFolders(file).ToArray(), "clean removes the classes of a single file");
            Assert.IsNull(toolchain.Clean(file));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public void AChosenJdkRunsItsOwnToolsAndSetsJavaHome()
    {
        string home = Path.Combine(Path.GetTempPath(), "jdk-21.0.13.1+2");
        var toolchain = new JavaToolchain { JavaHome = home };
        var file = new DevelopmentProject("Main.java", DevelopmentLanguage.Java, DevelopmentProjectKind.Script, Path.Combine("src", "Main.java"));
        var module = new DevelopmentProject("svc", DevelopmentLanguage.Java, DevelopmentProjectKind.JavaProject, Path.Combine("svc", "pom.xml"));

        ProcessCommand build = toolchain.Build(file)!;
        Assert.AreEqual(Path.Combine(home, "bin", Exe("javac")), build.FileName);
        Assert.AreEqual(home, build.Environment!["JAVA_HOME"]);
        Assert.AreEqual(Path.Combine(home, "bin", Exe("java")), toolchain.Run(file)!.FileName);

        // Maven and Gradle find the JDK through JAVA_HOME.
        ProcessCommand maven = toolchain.Test(module)!;
        Assert.AreEqual("mvn", maven.FileName);
        Assert.AreEqual(home, maven.Environment!["JAVA_HOME"]);
        Assert.IsNull(new JavaToolchain().Test(module)!.Environment, "without a choice the environment is the process's own");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AJavaProgramIsBuiltAndRunThroughTheProcessRunner()
    {
        // The JDK is not started: the runner stands in for javac and java, and only the commands and their output are checked.
        string folder = Path.Combine(Path.GetTempPath(), $"JavaRun_{Guid.NewGuid():N}");
        try
        {
            string main = WritePackageTree(folder);
            var file = new DevelopmentProject("Main.java", DevelopmentLanguage.Java, DevelopmentProjectKind.Script, main);
            var toolchain = new JavaToolchain();
            var runner = new FakeProcessRunner { OutputFor = command => Path.GetFileNameWithoutExtension(command.FileName) == "java" ? ["hi"] : [] };

            int built = await runner.RunAsync(toolchain.Build(file)!, _ => { });
            Assert.AreEqual(0, built, "the build's exit code is the javac exit code");

            var output = new List<string>();
            int ran = await runner.RunAsync(toolchain.Run(file)!, output.Add);
            Assert.AreEqual(0, ran);
            CollectionAssert.Contains(output, "hi");
            Assert.AreEqual(2, runner.Commands.Count);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
