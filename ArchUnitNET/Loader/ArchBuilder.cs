//  Copyright 2019 Florian Gather <florian.gather@tngtech.com>
// 	Copyright 2019 Paula Ruiz <paularuiz22@gmail.com>
// 	Copyright 2019 Fritz Brandhuber <fritz.brandhuber@tngtech.com>
//
// 	SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using System.Linq;
using ArchUnitNET.Domain;
using ArchUnitNET.Domain.Extensions;
using ArchUnitNET.Loader.LoadTasks;
using JetBrains.Annotations;
using Mono.Cecil;
using GenericParameter = ArchUnitNET.Domain.GenericParameter;

namespace ArchUnitNET.Loader
{
    internal class ArchBuilder
    {
        private readonly ArchitectureCache _architectureCache;
        private readonly ArchitectureCacheKey _architectureCacheKey;
        private readonly IDictionary<string, IType> _architectureTypes =
            new Dictionary<string, IType>();
        private readonly AssemblyRegistry _assemblyRegistry;
        private readonly LoadTaskRegistry _loadTaskRegistry;
        private readonly NamespaceRegistry _namespaceRegistry;
        private readonly TypeFactory _typeFactory;

        public ArchBuilder()
        {
            _assemblyRegistry = new AssemblyRegistry();
            _namespaceRegistry = new NamespaceRegistry();
            _loadTaskRegistry = new LoadTaskRegistry();
            var typeRegistry = new TypeRegistry();
            var methodMemberRegistry = new MethodMemberRegistry();
            _typeFactory = new TypeFactory(
                typeRegistry,
                methodMemberRegistry,
                _loadTaskRegistry,
                _assemblyRegistry,
                _namespaceRegistry
            );
            _architectureCacheKey = new ArchitectureCacheKey();
            _architectureCache = ArchitectureCache.Instance;
        }

        public IEnumerable<IType> Types => _architectureTypes.Select(pair => pair.Value);
        public IEnumerable<Assembly> Assemblies => _assemblyRegistry.Assemblies;
        public IEnumerable<Namespace> Namespaces => _namespaceRegistry.Namespaces;

        public void AddAssembly(
            [NotNull] AssemblyDefinition moduleAssembly,
            bool isOnlyReferenced,
            [CanBeNull] IEnumerable<AssemblyNameReference> moduleReferences
        )
        {
            var references = moduleReferences?.Select(reference => reference.Name).ToList();

            if (!_assemblyRegistry.ContainsAssembly(moduleAssembly.Name.FullName))
            {
                var assembly = _assemblyRegistry.GetOrCreateAssembly(
                    moduleAssembly.Name.FullName,
                    moduleAssembly.FullName,
                    isOnlyReferenced,
                    references
                );
                _loadTaskRegistry.Add(
                    typeof(CollectAssemblyAttributes),
                    new CollectAssemblyAttributes(assembly, moduleAssembly, _typeFactory)
                );
            }
        }

        public void LoadTypesForModule(ModuleDefinition module, string namespaceFilter)
        {
            _architectureCacheKey.Add(module.Name, namespaceFilter);

            var types = module.Types.First().FullName.Contains("<Module>")
                ? module.Types.Skip(1).ToList()
                : module.Types.ToList();

            types = types
                .Where(t =>
                    t.FullName != "Microsoft.CodeAnalysis.EmbeddedAttribute"
                    && t.FullName != "System.Runtime.CompilerServices.NullableAttribute"
                    && t.FullName != "System.Runtime.CompilerServices.NullableContextAttribute"
                    && !t.FullName.StartsWith("Coverlet")
                )
                .ToList();

            var nestedTypes = types;
            while (nestedTypes.Any())
            {
                nestedTypes = nestedTypes
                    .SelectMany(typeDefinition =>
                        typeDefinition.NestedTypes.Where(type => !type.IsCompilerGenerated())
                    )
                    .ToList();
                types.AddRange(nestedTypes);
            }

            var currentTypes = new List<IType>(types.Count);
            types
                .Where(typeDefinition =>
                    RegexUtils.MatchNamespaces(namespaceFilter, typeDefinition.Namespace)
                    && typeDefinition.CustomAttributes.All(att =>
                        att.AttributeType.FullName
                        != "Microsoft.VisualStudio.TestPlatform.TestSDKAutoGeneratedCode"
                    )
                )
                .ForEach(typeDefinition =>
                {
                    var type = _typeFactory.GetOrCreateTypeFromTypeReference(typeDefinition);
                    if (!_architectureTypes.ContainsKey(type.FullName) && !type.IsCompilerGenerated)
                    {
                        currentTypes.Add(type);
                        _architectureTypes.Add(type.FullName, type);
                    }
                });

            _namespaceRegistry
                .Namespaces.Where(ns => RegexUtils.MatchNamespaces(namespaceFilter, ns.FullName))
                .ForEach(ns =>
                {
                    _loadTaskRegistry.Add(
                        typeof(AddTypesToNamespace),
                        new AddTypesToNamespace(ns, currentTypes)
                    );
                });
        }

        private void UpdateTypeDefinitions()
        {
            _loadTaskRegistry.ExecuteTasks(
                new List<System.Type>
                {
                    typeof(AddMembers),
                    typeof(AddGenericParameterDependencies),
                    typeof(AddAttributesAndAttributeDependencies),
                    typeof(CollectAssemblyAttributes),
                    typeof(AddFieldAndPropertyDependencies),
                    typeof(AddMethodDependencies),
                    typeof(AddGenericArgumentDependencies),
                    typeof(AddClassDependencies),
                    typeof(AddBackwardsDependencies),
                    typeof(AddTypesToNamespace)
                }
            );
        }

        public Architecture Build()
        {
            var architecture = _architectureCache.TryGetArchitecture(_architectureCacheKey);
            if (architecture != null)
            {
                return architecture;
            }

            UpdateTypeDefinitions();
            var allTypes = _typeFactory.GetAllNonCompilerGeneratedTypes().ToList();
            var genericParameters = allTypes.OfType<GenericParameter>().ToList();
            var referencedTypes = allTypes.Except(Types).Except(genericParameters);
            var namespaces = Namespaces.Where(ns => ns.Types.Any());
            var newArchitecture = new Architecture(
                Assemblies,
                namespaces,
                Types,
                genericParameters,
                referencedTypes
            );
            _architectureCache.Add(_architectureCacheKey, newArchitecture);
            return newArchitecture;
        }
    }
}
