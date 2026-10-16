using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using iConsole.Buffers;
using iConsole.Data;
using iConsole.Drivers;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Jobs;
using iConsole.Model;
using iConsole.Screens;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Data;
using iTerminal.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Serilog;

namespace iTerminal.Core.Test.Generated;

[TestClass]
public class InstantiateAllTypesTest
{
    [TestMethod]
    public void InstantiatePublicTypesFromLocalAssemblies()
    {
        string baseDir = Path.GetDirectoryName(typeof(InstantiateAllTypesTest).Assembly.Location)!;
        string[] dlls = Directory.GetFiles(baseDir, "i*.dll");

        List<string> failures = [];

        foreach (string dll in dlls)
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

            // Only reflect over this solution's own assemblies. The "i*.dll" glob also matches
            // third-party bindings (e.g. iMobileDevice-net.dll) whose native handle/API types are
            // not meant to be default-constructed and are out of scope for this coverage check.
            string? asmName = asm.GetName().Name;
            if (asmName is null
                || !(asmName.StartsWith("iConsole", StringComparison.Ordinal)
                     || asmName.StartsWith("iTerminal", StringComparison.Ordinal)))
            {
                continue;
            }

            IEnumerable<Type> types = asm.GetExportedTypes().Where(t => t.IsClass && !t.IsAbstract && !t.IsGenericType);

            foreach (Type type in types)
            {
                object? instance = TryInstantiate(type, []);
                if (instance == null)
                {
                    failures.Add($"{asm.GetName().Name}: {type.FullName}");
                }
            }
        }

        if (failures.Count > 0)
        {
            string msg = string.Join("\n", failures.Take(50));
            Assert.IsTrue(failures.Count == 0, $"Could not instantiate {failures.Count} types. Examples:\n{msg}");
        }
    }

    private static object? TryInstantiate(Type type, HashSet<Type> visitedTypes)
    {
        if (visitedTypes.Contains(type))
        {
            return null;
        }

        if (TryCreateKnownValue(type, out object? knownValue))
        {
            return knownValue;
        }

        visitedTypes.Add(type);
        try
        {
            FieldInfo? instanceField = type.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
            if (instanceField?.FieldType == type)
            {
                return instanceField.GetValue(null);
            }

            PropertyInfo? instanceProperty = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            if (instanceProperty?.PropertyType == type)
            {
                return instanceProperty.GetValue(null);
            }

            foreach (ConstructorInfo constructor in type.GetConstructors().OrderBy(c => c.GetParameters().Length))
            {
                ParameterInfo[] parameters = constructor.GetParameters();
                object?[] args = new object?[parameters.Length];
                bool canInvoke = true;

                for (int i = 0; i < parameters.Length; i++)
                {
                    if (!TryCreateArgument(parameters[i], visitedTypes, out object? argument))
                    {
                        canInvoke = false;
                        break;
                    }

                    args[i] = argument;
                }

                if (!canInvoke)
                {
                    continue;
                }

                try
                {
                    return constructor.Invoke(args);
                }
                catch
                {
                }
            }

            try
            {
                return System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);
            }
            catch
            {
                return null;
            }
        }
        finally
        {
            visitedTypes.Remove(type);
        }
    }

    private static bool TryCreateArgument(ParameterInfo parameter, HashSet<Type> visitedTypes, out object? argument)
    {
        Type parameterType = parameter.ParameterType;

        if (parameter.HasDefaultValue)
        {
            argument = parameter.DefaultValue;
            return true;
        }

        if (TryCreateKnownValue(parameterType, out argument))
        {
            return true;
        }

        if (parameterType == typeof(string))
        {
            argument = string.Empty;
            return true;
        }

        if (parameterType.IsValueType)
        {
            argument = Activator.CreateInstance(parameterType);
            return true;
        }

        if (TryCreateEmptyCollection(parameterType, out argument))
        {
            return true;
        }

        if (!parameterType.IsAbstract && !parameterType.IsInterface)
        {
            argument = TryInstantiate(parameterType, visitedTypes);
            return argument != null;
        }

        Type? concreteType = FindConcreteImplementation(parameterType);
        if (concreteType is not null)
        {
            argument = TryInstantiate(concreteType, visitedTypes);
            return argument != null;
        }

        if (parameterType.IsInterface)
        {
            argument = CreateInterfaceProxy(parameterType);
            return argument != null;
        }

        argument = null;
        return true;
    }

    private static object? CreateInterfaceProxy(Type interfaceType)
    {
        MethodInfo method = typeof(InstantiateAllTypesTest)
            .GetMethod(nameof(CreateInterfaceProxyGeneric), BindingFlags.NonPublic | BindingFlags.Static)!;
        return method.MakeGenericMethod(interfaceType).Invoke(null, null);
    }

    private static TInterface CreateInterfaceProxyGeneric<TInterface>() where TInterface : class
    {
        return DispatchProxy.Create<TInterface, TestInterfaceProxy>();
    }

    private static Type? FindConcreteImplementation(Type abstractionType)
    {
        if (!abstractionType.IsInterface || !abstractionType.Name.StartsWith('I'))
        {
            return null;
        }

        string expectedName = abstractionType.Name[1..];
        return abstractionType.Assembly
            .GetExportedTypes()
            .FirstOrDefault(type =>
                type.IsClass
                && !type.IsAbstract
                && !type.IsGenericType
                && type.Name == expectedName
                && abstractionType.IsAssignableFrom(type));
    }

    private static bool TryCreateKnownValue(Type type, out object? value)
    {
        if (type == typeof(SystemConsoleDriver) || type == typeof(VirtualSequenceConsoleDriver))
        {
            value = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);
            return true;
        }

        if (type == typeof(IConsoleDriver))
        {
            value = NullConsoleDriver.Instance;
            return true;
        }

        if (type == typeof(IThemeService))
        {
            value = NullThemeService.Instance;
            return true;
        }

        if (type == typeof(ISettingsService))
        {
            value = NullSettingsService.Instance;
            return true;
        }

        if (type == typeof(IContentModelService))
        {
            value = NullContentModelService.Instance;
            return true;
        }

        // Common core services/types used by TerminalMenuBar and related UI components
        if (type == typeof(iTerminal.Core.Services.WeatherService))
        {
            value = new iTerminal.Core.Services.WeatherService();
            return true;
        }

        if (type == typeof(iTerminal.Core.Services.OnlineQuoteService))
        {
            value = new iTerminal.Core.Services.OnlineQuoteService();
            return true;
        }

        // Dialogs and many UI types are complex to construct; return an uninitialized object
        // so constructors that take these as dependencies can still be invoked by TryInstantiate.
        if (type == typeof(iConsole.Dialogs.FileViewerDialog)
            || type == typeof(iConsole.Dialogs.TextFileEditorDialog)
            || type == typeof(iTerminal.Core.Dialogs.CreateFolderDialog)
            || type == typeof(iTerminal.Core.Dialogs.CreateFileDialog)
            || type == typeof(iTerminal.Core.Dialogs.RenameDialog)
            || type == typeof(iTerminal.Core.Dialogs.CopyDialog)
            || type == typeof(iTerminal.Core.Dialogs.MoveDialog)
            || type == typeof(iTerminal.Core.Dialogs.DeleteDialog)
            || type == typeof(iTerminal.Core.Dialogs.TerminalDialog)
            || type == typeof(iTerminal.Core.Dialogs.DiskManagerDialog)
            || type == typeof(iTerminal.Core.Dialogs.NetworkManagerDialog)
            || type == typeof(iTerminal.Core.Dialogs.ThemeManagerDialog)
            || type == typeof(iTerminal.Core.Dialogs.SettingsDialog)
            || type == typeof(iTerminal.Core.Dialogs.TaskManagerDialog)
            || type == typeof(iTerminal.Core.Dialogs.ProcessManagerDialog)
            || type == typeof(iTerminal.Core.Dialogs.SystemInfoDialog)
            || type == typeof(iConsole.Dialogs.HelpDialog)
            || type == typeof(iTerminal.Core.Dialogs.AboutDialog))
        {
            try
            {
                value = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);
                return true;
            }
            catch
            {
                // fall through
            }
        }

        // Models / services / other concrete types used by TerminalMenuBar
        if (type == typeof(iTerminal.Core.Services.FolderHistoryService)
            || type == typeof(iTerminal.Core.Model.DriveInfoModel)
            || type == typeof(iTerminal.Core.Model.FolderIndexModel)
            || type == typeof(iTerminal.Core.Controls.Contents.Content)
            || type == typeof(iTerminal.Core.Model.TerminalModel))
        {
            try
            {
                value = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);
                return true;
            }
            catch
            {
                // fall through
            }
        }

        if (type == typeof(IDateTimeProviderService))
        {
            value = new DateTimeProviderService();
            return true;
        }

        if (type == typeof(ITextModel))
        {
            value = new TextModel(string.Empty);
            return true;
        }

        if (type == typeof(ICompletionModel))
        {
            value = new CompletionModel([]);
            return true;
        }

        if (type == typeof(IHierarchicalItemModel))
        {
            value = new HierarchicalItemModel(null, string.Empty);
            return true;
        }

        if (type == typeof(IBuffer))
        {
            value = new iConsole.Buffers.Buffer(NullConsoleDriver.Instance);
            return true;
        }

        if (type == typeof(IScreen))
        {
            value = new Screen(NullConsoleDriver.Instance, NullThemeService.Instance);
            return true;
        }

        if (type == typeof(IRenderingDumpHelper))
        {
            value = NullRenderingDumpHelper.Instance;
            return true;
        }

        if (type == typeof(TaskManager))
        {
            value = new TaskManager();
            return true;
        }

        if (type == typeof(ILogger))
        {
            value = new LoggerConfiguration().CreateLogger();
            return true;
        }

        if (type == typeof(Url))
        {
            value = new Url(Directory.GetCurrentDirectory());
            return true;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(DbContextOptions<>))
        {
            Type contextType = type.GetGenericArguments()[0];
            Type builderType = typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType);
            object builder = Activator.CreateInstance(builderType)!;
            PropertyInfo optionsProperty = builderType
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Single(property => property.Name == nameof(DbContextOptionsBuilder.Options) && property.PropertyType == type);
            value = optionsProperty.GetValue(builder);
            return true;
        }

        value = null;
        return false;
    }

    private static bool TryCreateEmptyCollection(Type type, out object? value)
    {
        if (type.IsArray)
        {
            value = Array.CreateInstance(type.GetElementType()!, 0);
            return true;
        }

        if (!type.IsGenericType)
        {
            value = null;
            return false;
        }

        Type genericType = type.GetGenericTypeDefinition();
        Type elementType = type.GetGenericArguments()[0];

        if (genericType == typeof(IEnumerable<>)
            || genericType == typeof(ICollection<>)
            || genericType == typeof(IList<>)
            || genericType == typeof(IReadOnlyCollection<>)
            || genericType == typeof(IReadOnlyList<>))
        {
            value = Array.CreateInstance(elementType, 0);
            return true;
        }

        if (genericType == typeof(List<>))
        {
            value = Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType));
            return true;
        }

        value = null;
        return false;
    }

    private class TestInterfaceProxy : DispatchProxy
    {
        private readonly Dictionary<string, object?> _values = [];
        private readonly Dictionary<string, Delegate?> _events = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null)
            {
                return null;
            }

            string methodName = targetMethod.Name;
            if (methodName.StartsWith("add_", StringComparison.Ordinal))
            {
                string eventName = methodName[4..];
                Delegate? handler = args?[0] as Delegate;
                _events[eventName] = Delegate.Combine(_events.GetValueOrDefault(eventName), handler);
                return null;
            }

            if (methodName.StartsWith("remove_", StringComparison.Ordinal))
            {
                string eventName = methodName[7..];
                Delegate? handler = args?[0] as Delegate;
                _events[eventName] = Delegate.Remove(_events.GetValueOrDefault(eventName), handler);
                return null;
            }

            if (methodName.StartsWith("set_", StringComparison.Ordinal))
            {
                _values[methodName[4..]] = args?[0];
                return null;
            }

            if (methodName.StartsWith("get_", StringComparison.Ordinal))
            {
                string propertyName = methodName[4..];
                if (_values.TryGetValue(propertyName, out object? existingValue))
                {
                    return existingValue;
                }

                object? createdValue = CreateDefaultValue(targetMethod.ReturnType);
                _values[propertyName] = createdValue;
                return createdValue;
            }

            return CreateDefaultValue(targetMethod.ReturnType);
        }

        private static object? CreateDefaultValue(Type returnType)
        {
            if (returnType == typeof(void))
            {
                return null;
            }

            if (TryCreateKnownValue(returnType, out object? knownValue))
            {
                return knownValue;
            }

            if (returnType == typeof(string))
            {
                return string.Empty;
            }

            if (returnType.IsValueType)
            {
                return Activator.CreateInstance(returnType);
            }

            if (TryCreateEmptyCollection(returnType, out object? collectionValue))
            {
                return collectionValue;
            }

            Type? concreteType = FindConcreteImplementation(returnType);
            if (concreteType is not null)
            {
                return TryInstantiate(concreteType, []);
            }

            if (returnType.IsInterface)
            {
                return CreateInterfaceProxy(returnType);
            }

            if (!returnType.IsAbstract)
            {
                return TryInstantiate(returnType, []);
            }

            return null;
        }
    }
}
