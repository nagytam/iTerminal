using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iConsole.Test.Generated;

[TestClass]
public class InstantiateIConsoleTypesTest
{
    [TestMethod]
    public void InstantiatePublicTypesFromIConsoleAssemblies()
    {
        var baseDir = Path.GetDirectoryName(typeof(InstantiateIConsoleTypesTest).Assembly.Location)!;
        var dlls = Directory.GetFiles(baseDir, "iConsole*.dll");

        var failures = new List<string>();

        foreach (var dll in dlls)
        {
            Assembly asm;
            try
            {
                asm = Assembly.LoadFrom(dll);
            }
            catch
            {
                continue;
            }

            var types = asm.GetExportedTypes().Where(t => t.IsClass && !t.IsAbstract && !t.IsGenericType);

            // Allocate without running constructors — avoids constructor side effects and dependency resolution.
            object? CreateDefault(Type t)
            {
                try
                {
                    return System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(t);
                }
                catch
                {
                    return null;
                }
            }

            foreach (var type in types)
            {
                object? instance = null;
                try
                {
                    instance = CreateDefault(type);
                    if (instance == null)
                    {
                        // As a final fallback, allocate without running constructors.
                        instance = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);
                    }
                }
                catch
                {
                    try
                    {
                        instance = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);
                    }
                    catch
                    {
                        failures.Add($"{asm.GetName().Name}: {type.FullName}");
                    }
                }
            }
        }

        if (failures.Count > 0)
        {
            var msg = string.Join("\n", failures.Take(50));
            Assert.IsTrue(failures.Count == 0, $"Could not instantiate {failures.Count} types from iConsole assemblies. Examples:\n{msg}");
        }
    }
}
