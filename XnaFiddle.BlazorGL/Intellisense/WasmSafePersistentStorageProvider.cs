using System;
using System.Composition;
using System.Reflection;
using System.Reflection.Emit;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Host.Mef;

namespace XnaFiddle.Intellisense
{
    /// <summary>
    /// Emits, at runtime, a MEF part that implements Roslyn's internal
    /// <c>Microsoft.CodeAnalysis.Host.IPersistentStorageConfiguration</c> with a
    /// WASM-safe no-op. We can't author the type in C# because the interface (and
    /// its <c>SolutionKey</c> parameter) are <c>internal</c> to
    /// <c>Microsoft.CodeAnalysis.Workspaces</c>, so any direct reference would
    /// fail at compile time. Reflection.Emit sidesteps the C# visibility check
    /// (IL access rules still apply at runtime, but interface implementation of
    /// an internal interface is permitted when the implementing type lives in a
    /// separate assembly as long as the JIT sees the interface slot match — the
    /// CLR routes the virtual call through the interface map).
    ///
    /// The emitted part is decorated with:
    ///   <c>[ExportWorkspaceService(typeof(IPersistentStorageConfiguration), ServiceLayer.Host)]</c>
    ///   <c>[Shared]</c>
    /// so Roslyn's MEF composition finds it when code paths (notably
    /// <c>SymbolFinder.FindDeclarationsAsync</c>, used by the Add-using code
    /// action) request the configuration service. Without such a part those
    /// paths throw <see cref="InvalidOperationException"/> because we strip the
    /// built-in <c>DefaultPersistentStorageConfiguration</c> — its static ctor
    /// calls <c>Process.GetCurrentProcess()</c>, which is not supported on WASM.
    ///
    /// The implementation returns <c>ThrowOnFailure = false</c> and
    /// <c>TryGetStorageLocation(_) = null</c>, which matches how Roslyn behaves
    /// with no persistent storage available — callers fall back to in-memory /
    /// no-op storage.
    /// </summary>
    public static class WasmSafePersistentStorageProvider
    {
        private static Type _emittedType;
        private static readonly object _lock = new();

        /// <summary>
        /// Returns the runtime-emitted type that implements
        /// <c>IPersistentStorageConfiguration</c>. The type is emitted exactly
        /// once and cached. Returns <c>null</c> if the Roslyn interface can't
        /// be located (defensive: a future Roslyn upgrade could rename it, in
        /// which case we prefer to no-op rather than crash host init).
        /// </summary>
        public static Type GetOrCreateConfigurationType()
        {
            if (_emittedType != null) return _emittedType;

            lock (_lock)
            {
                if (_emittedType != null) return _emittedType;

                var workspacesAssembly = typeof(IWorkspaceService).Assembly;
                var configInterface = workspacesAssembly.GetType(
                    "Microsoft.CodeAnalysis.Host.IPersistentStorageConfiguration",
                    throwOnError: false);
                if (configInterface == null) return null;

                var solutionKeyType = workspacesAssembly.GetType(
                    "Microsoft.CodeAnalysis.Storage.SolutionKey",
                    throwOnError: false);
                if (solutionKeyType == null) return null;

                _emittedType = EmitConfigurationType(configInterface, solutionKeyType);
                return _emittedType;
            }
        }

        private static Type EmitConfigurationType(Type configInterface, Type solutionKeyType)
        {
            var asmName = new AssemblyName("XnaFiddle.DynamicWorkspaceServices");
            var asmBuilder = AssemblyBuilder.DefineDynamicAssembly(asmName, AssemblyBuilderAccess.Run);
            var moduleBuilder = asmBuilder.DefineDynamicModule("main");

            var typeBuilder = moduleBuilder.DefineType(
                "XnaFiddle.Intellisense.Emitted.WasmSafePersistentStorageConfiguration",
                TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
                parent: typeof(object),
                interfaces: [typeof(IWorkspaceService), configInterface]);

            // Parameterless public ctor — required for MEF activation.
            var ctor = typeBuilder.DefineConstructor(
                MethodAttributes.Public | MethodAttributes.HideBySig
                    | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                CallingConventions.Standard,
                parameterTypes: Type.EmptyTypes);
            var ctorIl = ctor.GetILGenerator();
            ctorIl.Emit(OpCodes.Ldarg_0);
            ctorIl.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
            ctorIl.Emit(OpCodes.Ret);

            // bool ThrowOnFailure { get; } => false
            // The interface defines an instance property with a getter; we implement
            // it as a non-virtual-looking virtual (Final + Virtual + NewSlot) so the
            // CLR wires it into the interface map.
            var throwOnFailureGetter = typeBuilder.DefineMethod(
                "get_ThrowOnFailure",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName
                    | MethodAttributes.NewSlot | MethodAttributes.Virtual | MethodAttributes.Final,
                typeof(bool),
                Type.EmptyTypes);
            var throwIl = throwOnFailureGetter.GetILGenerator();
            throwIl.Emit(OpCodes.Ldc_I4_0);
            throwIl.Emit(OpCodes.Ret);

            var throwProp = typeBuilder.DefineProperty(
                "ThrowOnFailure", PropertyAttributes.None, typeof(bool), Type.EmptyTypes);
            throwProp.SetGetMethod(throwOnFailureGetter);
            var iGet = configInterface.GetProperty("ThrowOnFailure")!.GetGetMethod(nonPublic: true)!;
            typeBuilder.DefineMethodOverride(throwOnFailureGetter, iGet);

            // string TryGetStorageLocation(SolutionKey) => null
            var tryGet = typeBuilder.DefineMethod(
                "TryGetStorageLocation",
                MethodAttributes.Public | MethodAttributes.HideBySig
                    | MethodAttributes.NewSlot | MethodAttributes.Virtual | MethodAttributes.Final,
                typeof(string),
                [solutionKeyType]);
            var tryIl = tryGet.GetILGenerator();
            tryIl.Emit(OpCodes.Ldnull);
            tryIl.Emit(OpCodes.Ret);

            var iTryGet = configInterface.GetMethod(
                "TryGetStorageLocation", [solutionKeyType])!;
            typeBuilder.DefineMethodOverride(tryGet, iTryGet);

            // [ExportWorkspaceService(typeof(IPersistentStorageConfiguration), ServiceLayer.Host)]
            var exportCtor = typeof(ExportWorkspaceServiceAttribute).GetConstructor(
                [typeof(Type), typeof(string)])!;
            typeBuilder.SetCustomAttribute(new CustomAttributeBuilder(
                exportCtor,
                [configInterface, ServiceLayer.Host]));

            // [Shared] from System.Composition so MEF treats the export as a singleton.
            var sharedCtor = typeof(SharedAttribute).GetConstructor(Type.EmptyTypes)!;
            typeBuilder.SetCustomAttribute(new CustomAttributeBuilder(sharedCtor, []));

            return typeBuilder.CreateType()!;
        }
    }
}
